"""MkDocs hooks for the SqlSense site.

- Serve the user guide straight from deploy/, so the site, the .md and the package's .docx share one source.
- Vietnamese heading anchors: the default slugify drops "đ" (it has no decomposed form), giving "#1-cai-at" for
  "1. Cài đặt"; map it to "d" first.
"""
import os

from markdown.extensions.toc import slugify
from mkdocs.structure.files import File

GUIDE = os.path.join(os.path.dirname(__file__), "..", "deploy", "HUONG-DAN-SU-DUNG.md")


def vietnamese_slugify(value, separator):
    return slugify(value.replace("đ", "d").replace("Đ", "D"), separator)


def on_config(config):
    config.mdx_configs.setdefault("toc", {})["slugify"] = vietnamese_slugify
    return config


def on_files(files, config):
    files.append(File.generated(config, "huong-dan.md", abs_src_path=os.path.abspath(GUIDE)))
    return files
