"""MkDocs hooks for the SqlSense site (used by both mkdocs.yml and mkdocs.en.yml).

- Serve the user guide straight from deploy/, so the site, the .md and the package's .docx share one source:
  Vietnamese deploy/HUONG-DAN-SU-DUNG.md as huong-dan.md, English deploy/USER-GUIDE.md as user-guide.md.
- Shared files (favicon) come from website/assets, so each language's docs_dir holds only its pages.
- Vietnamese heading anchors: the default slugify drops "đ" (it has no decomposed form), giving "#1-cai-at" for
  "1. Cài đặt"; map it to "d" first.
"""
import os

from markdown.extensions.toc import slugify
from mkdocs.structure.files import File

HERE = os.path.dirname(os.path.abspath(__file__))
DEPLOY = os.path.join(HERE, "..", "deploy")
ASSETS = os.path.join(HERE, "assets")

GUIDES = {
    "vi": ("huong-dan.md", "HUONG-DAN-SU-DUNG.md"),
    "en": ("user-guide.md", "USER-GUIDE.md"),
}


def vietnamese_slugify(value, separator):
    return slugify(value.replace("đ", "d").replace("Đ", "D"), separator)


def on_config(config):
    config.mdx_configs.setdefault("toc", {})["slugify"] = vietnamese_slugify
    return config


def on_files(files, config):
    page, source = GUIDES[config.theme["language"]]
    files.append(File.generated(config, page, abs_src_path=os.path.abspath(os.path.join(DEPLOY, source))))
    for name in os.listdir(ASSETS):
        files.append(File.generated(config, "assets/" + name, abs_src_path=os.path.join(ASSETS, name)))
    return files
