"""MkDocs hooks for the SqlSense site (used by both mkdocs.yml, English, and mkdocs.vi.yml, Vietnamese).

- Serve the user guide straight from deploy/, so the site, the .md and the package's .docx share one source:
  Vietnamese deploy/HUONG-DAN-SU-DUNG.md as huong-dan.md, English deploy/USER-GUIDE.md as user-guide.md.
- Shared files (favicon, language.js) come from website/assets, so each language's docs_dir holds only its pages.
- Redirect pages for addresses that moved when English became the default language.
- Vietnamese heading anchors: the default slugify drops "đ" (it has no decomposed form), giving "#1-cai-at" for
  "1. Cài đặt"; map it to "d" first.
"""
import os
from urllib.parse import urlsplit

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


# Old addresses -> new ones, written as small redirect pages by the root (English) build. /huong-dan/ was the
# Vietnamese guide while Vietnamese was the default (and is printed in released guides); /en/ briefly held English.
REDIRECTS = {
    "huong-dan": "vi/huong-dan/",
    "en": "",
    "en/user-guide": "user-guide/",
}

REDIRECT_PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Redirecting…</title>
<link rel="canonical" href="{url}">
<meta name="robots" content="noindex">
<meta http-equiv="refresh" content="0; url={url}">
<script>location.replace("{url}" + location.hash);</script>
</head>
<body><a href="{url}">{url}</a></body>
</html>
"""


def on_post_build(config):
    if config.theme["language"] != "en":
        return
    root = urlsplit(config.site_url).path.rstrip("/") + "/"  # /SqlSense/: also works in a local preview
    for old, new in REDIRECTS.items():
        folder = os.path.join(config.site_dir, *old.split("/"))
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "index.html"), "w", encoding="utf-8") as f:
            f.write(REDIRECT_PAGE.format(url=root + new))


def on_files(files, config):
    page, source = GUIDES[config.theme["language"]]
    files.append(File.generated(config, page, abs_src_path=os.path.abspath(os.path.join(DEPLOY, source))))
    for name in os.listdir(ASSETS):
        files.append(File.generated(config, "assets/" + name, abs_src_path=os.path.join(ASSETS, name)))
    return files
