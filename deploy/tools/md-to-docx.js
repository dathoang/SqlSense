// Renders the user guide (Markdown) as a Word document for the deploy package.
// Usage: node md-to-docx.js <input.md> <output.docx> [version]
// Handles the Markdown the guide uses: headings, paragraphs, bold/italic/code/links, bullet and numbered
// lists, tables, fenced code, block quotes and rules.
'use strict';

const fs = require('fs');
const path = require('path');
const { lexer } = require('marked');
const {
  AlignmentType, BorderStyle, Document, ExternalHyperlink, Footer, HeadingLevel, LevelFormat, Packer,
  PageNumber, Paragraph, ShadingType, Table, TableCell, TableRow, TextRun, WidthType,
} = require('docx');

const [input, output, version] = process.argv.slice(2);
if (!input || !output) {
  console.error('usage: node md-to-docx.js <input.md> <output.docx> [version]');
  process.exit(2);
}

const FONT = 'Segoe UI';
const MONO = 'Consolas';
const ACCENT = '1F4E79';
const CODE_FILL = 'F2F4F7';
const BORDER = 'C8CDD3';
// A4, 2 cm margins.
const PAGE = { width: 11906, height: 16838, margin: 1134 };
const TEXT_WIDTH = PAGE.width - 2 * PAGE.margin;

// ---------------- inline ----------------

function decode(text) {
  return text.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

/** Inline tokens → TextRun / ExternalHyperlink children. */
function inline(tokens, style = {}) {
  const runs = [];
  for (const t of tokens || []) {
    switch (t.type) {
      case 'strong':
        runs.push(...inline(t.tokens, { ...style, bold: true }));
        break;
      case 'em':
        runs.push(...inline(t.tokens, { ...style, italics: true }));
        break;
      case 'codespan':
        runs.push(new TextRun({ ...style, text: decode(t.text), font: MONO, shading: { type: ShadingType.CLEAR, fill: CODE_FILL, color: 'auto' } }));
        break;
      case 'link':
        runs.push(new ExternalHyperlink({ link: t.href, children: inline(t.tokens, { ...style, style: 'Hyperlink' }) }));
        break;
      case 'br':
        runs.push(new TextRun({ ...style, break: 1 }));
        break;
      case 'text':
        if (t.tokens && t.tokens.length) runs.push(...inline(t.tokens, style));
        else runs.push(new TextRun({ ...style, text: decode(t.text).replace(/\n/g, ' ') }));
        break;
      case 'escape':
      case 'html':
        runs.push(new TextRun({ ...style, text: decode(t.text) }));
        break;
      default:
        if (t.raw) runs.push(new TextRun({ ...style, text: decode(t.raw) }));
    }
  }
  return runs;
}

// ---------------- blocks ----------------

// One numbering definition per numbered list, so every list restarts at 1 in any renderer.
const numberedLists = [];
let firstH2 = true;

function heading(t) {
  const level = [HeadingLevel.TITLE, HeadingLevel.HEADING_1, HeadingLevel.HEADING_2, HeadingLevel.HEADING_3][Math.min(t.depth, 3) - 1];
  // Each top-level section starts a page, as in a printed manual.
  const pageBreakBefore = t.depth === 2 && !firstH2;
  if (t.depth === 2) firstH2 = false;
  return [new Paragraph({ heading: level, pageBreakBefore, keepNext: true, children: inline(t.tokens) })];
}

function code(t) {
  const lines = t.text.replace(/\t/g, '    ').split('\n');
  const border = { style: BorderStyle.SINGLE, size: 4, color: BORDER, space: 4 };
  return lines.map((line, i) => new Paragraph({
    keepLines: true,
    keepNext: i < lines.length - 1,
    spacing: { before: i === 0 ? 80 : 0, after: i === lines.length - 1 ? 160 : 0, line: 260 },
    shading: { type: ShadingType.CLEAR, fill: CODE_FILL, color: 'auto' },
    border: { left: border, right: border, top: i === 0 ? border : undefined, bottom: i === lines.length - 1 ? border : undefined },
    children: [new TextRun({ text: line.length ? line : ' ', font: MONO, size: 18 })],
  }));
}

function list(t, level = 0) {
  let reference = 'bullets';
  if (t.ordered) {
    reference = 'numbers-' + (numberedLists.length + 1);
    numberedLists.push(reference);
  }
  const out = [];
  for (const item of t.items) {
    let first = true;
    for (const child of item.tokens) {
      if (child.type === 'list') {
        out.push(...list(child, level + 1));
        continue;
      }
      if (child.type !== 'text' && child.type !== 'paragraph') {
        out.push(...block(child));
        continue;
      }
      out.push(new Paragraph({
        numbering: first ? { reference, level } : undefined,
        indent: first ? undefined : { left: 720 * (level + 1) },
        spacing: { after: 80 },
        children: inline(child.tokens || [{ type: 'text', text: child.text }]),
      }));
      first = false;
    }
  }
  return out;
}

function table(t) {
  const columns = t.header.length;
  // Width by content: longest cell per column, clamped so short columns stay readable.
  const lengths = Array.from({ length: columns }, (_, c) =>
    Math.max(4, ...[t.header[c], ...t.rows.map(r => r[c])].map(cell => Math.min(60, (cell && cell.text || '').length))));
  const total = lengths.reduce((a, b) => a + b, 0);
  const widths = lengths.map(l => Math.floor(TEXT_WIDTH * l / total));
  widths[columns - 1] += TEXT_WIDTH - widths.reduce((a, b) => a + b, 0);

  const border = { style: BorderStyle.SINGLE, size: 4, color: BORDER };
  const borders = { top: border, bottom: border, left: border, right: border };
  const cell = (c, i, header) => new TableCell({
    width: { size: widths[i], type: WidthType.DXA },
    borders,
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    shading: header ? { type: ShadingType.CLEAR, fill: 'DCE6F0', color: 'auto' } : undefined,
    children: [new Paragraph({ children: inline(c.tokens, header ? { bold: true } : {}) })],
  });

  const rows = [];
  // A header of empty cells ("| | |") is just there to make the table: drop it.
  if (t.header.some(h => h.text.trim())) rows.push(new TableRow({ tableHeader: true, cantSplit: true, children: t.header.map((c, i) => cell(c, i, true)) }));
  for (const r of t.rows) rows.push(new TableRow({ cantSplit: true, children: r.map((c, i) => cell(c, i, false)) }));
  return [new Table({ width: { size: TEXT_WIDTH, type: WidthType.DXA }, columnWidths: widths, rows }), new Paragraph({ spacing: { after: 120 }, children: [] })];
}

function blockquote(t) {
  const border = { style: BorderStyle.SINGLE, size: 18, color: 'E8A33D', space: 8 };
  return t.tokens.flatMap(child => child.type === 'paragraph'
    ? [new Paragraph({
      border: { left: border },
      shading: { type: ShadingType.CLEAR, fill: 'FFF6E5', color: 'auto' },
      indent: { left: 200 },
      spacing: { before: 120, after: 160 },
      children: inline(child.tokens),
    })]
    : block(child));
}

function block(t) {
  switch (t.type) {
    case 'heading': return heading(t);
    case 'paragraph': return [new Paragraph({ spacing: { after: 140 }, children: inline(t.tokens) })];
    case 'code': return code(t);
    case 'list': return list(t);
    case 'table': return table(t);
    case 'blockquote': return blockquote(t);
    case 'hr':
    case 'space': return [];
    default: return t.text ? [new Paragraph({ children: [new TextRun(decode(t.text))] })] : [];
  }
}

// ---------------- document ----------------

const markdown = fs.readFileSync(input, 'utf8');
const children = lexer(markdown).flatMap(block);
const title = (markdown.match(/^#\s+(.+)$/m) || [, 'SqlSense'])[1];

const bulletLevels = [0, 1, 2].map(level => ({
  level, format: LevelFormat.BULLET, text: ['•', '◦', '▪'][level], alignment: AlignmentType.LEFT,
  style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } },
}));
const numberLevels = [0, 1, 2].map(level => ({
  level, format: [LevelFormat.DECIMAL, LevelFormat.LOWER_LETTER, LevelFormat.LOWER_ROMAN][level], text: `%${level + 1}.`,
  alignment: AlignmentType.LEFT,
  style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } },
}));

const doc = new Document({
  creator: 'SqlSense',
  title,
  description: 'SqlSense user guide' + (version ? ' ' + version : ''),
  styles: {
    default: { document: { run: { font: FONT, size: 21 }, paragraph: { spacing: { line: 276 } } } },
    paragraphStyles: [
      { id: 'Title', name: 'Title', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 40, bold: true, color: ACCENT }, paragraph: { spacing: { after: 240 } } },
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 32, bold: true, color: ACCENT },
        paragraph: { spacing: { before: 240, after: 160 }, outlineLevel: 0,
          border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: ACCENT, space: 4 } } } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 25, bold: true, color: ACCENT }, paragraph: { spacing: { before: 280, after: 120 }, outlineLevel: 1 } },
    ],
  },
  numbering: {
    config: [
      { reference: 'bullets', levels: bulletLevels },
      ...numberedLists.map(reference => ({ reference, levels: numberLevels })),
    ],
  },
  sections: [{
    properties: { page: { size: { width: PAGE.width, height: PAGE.height }, margin: { top: PAGE.margin, bottom: PAGE.margin, left: PAGE.margin, right: PAGE.margin } } },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.CENTER,
          children: [
            new TextRun({ text: 'SqlSense' + (version ? ' ' + version : '') + ' — Hướng dẫn sử dụng · Trang ', size: 16, color: '7F7F7F' }),
            new TextRun({ children: [PageNumber.CURRENT], size: 16, color: '7F7F7F' }),
            new TextRun({ text: ' / ', size: 16, color: '7F7F7F' }),
            new TextRun({ children: [PageNumber.TOTAL_PAGES], size: 16, color: '7F7F7F' }),
          ],
        })],
      }),
    },
    children,
  }],
});

Packer.toBuffer(doc).then(buffer => {
  fs.writeFileSync(output, buffer);
  console.log(`${path.basename(output)}: ${children.length} blocks, ${buffer.length.toLocaleString('en-US')} bytes`);
});
