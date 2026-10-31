"""Genera la Wiki de GitHub del repositorio desde el manual (docs/manual), en español e inglés.

La fuente es docs/manual: la Wiki es una copia que se vuelve a generar cuando el manual cambia, nunca se edita a
mano. Cada página del manual pasa a ser una página de la Wiki con el nombre de su título; se agregan el menú
lateral (_Sidebar.md) con las dos listas y un pie (_Footer.md). Los enlaces entre páginas pasan al formato de la
Wiki; las imágenes y los demás archivos del repositorio se toman de main.

    git clone https://github.com/darrenarauz21/claupet.wiki.git <carpeta>
    python herramientas/manual_a_wiki.py <carpeta>
    (en <carpeta>: git add -A, git commit y git push)

Solo usa la biblioteca estándar. Borra los .md de la carpeta antes de escribir, para que no queden páginas viejas.
"""
import os
import posixpath
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANUAL = "docs/manual"
REPO = "darrenarauz21/claupet"
RAW = f"https://raw.githubusercontent.com/{REPO}/main/"
BLOB = f"https://github.com/{REPO}/blob/main/"
# El orden del menú: el mismo del índice del manual.
ORDER = ["instalar", "primeros-pasos", "uso", "console", "terminal", "mascotas", "isla", "asistente-y-voz",
         "ajustes", "extensiones", "preguntas-frecuentes", "novedades"]
IMAGES = (".png", ".gif", ".jpg", ".jpeg", ".svg", ".webp")


def read(path):
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        return f.read().replace("\r\n", "\n")


def title(text):
    """El primer título (# …) de la página, sin el emoji del principio."""
    match = re.search(r"^# (.+)$", text, re.M)
    return re.sub(r"^[^\w¿¡(]+", "", match.group(1)).strip() if match else ""


def slug(name):
    """El nombre del archivo de la Wiki: los espacios pasan a guiones."""
    return re.sub(r"[^\w\s\-(),'.]", "", name).strip().replace(" ", "-")


def link(name):
    """El enlace a una página: su archivo sin .md, con los paréntesis codificados (cortarían el enlace de Markdown)."""
    return slug(name).replace("(", "%28").replace(")", "%29")


def pages():
    """Cada página del manual (ruta en el repositorio) y su nombre en la Wiki."""
    names = {f"{MANUAL}/README.md": "Home"}
    english_home = title(read(f"{MANUAL}/en/README.md")) or "Claupet user guide"
    names[f"{MANUAL}/en/README.md"] = english_home
    spanish = {}
    for page in ORDER:
        spanish[page] = title(read(f"{MANUAL}/es/{page}.md"))
        names[f"{MANUAL}/es/{page}.md"] = spanish[page]
    for page in ORDER:
        name = title(read(f"{MANUAL}/en/{page}.md"))
        # «Claupet Terminal» se llama igual en los dos idiomas: la inglesa lleva (EN).
        names[f"{MANUAL}/en/{page}.md"] = f"{name} (EN)" if name == spanish[page] else name
    return names


def convert(path, text, names):
    """Los enlaces de una página: a otra página del manual, al formato de la Wiki; lo demás, a main."""
    folder = posixpath.dirname(path)

    def target(href):
        if re.match(r"^[a-z]+:", href) or href.startswith("#"):
            return href
        file, _, anchor = href.partition("#")
        resolved = posixpath.normpath(posixpath.join(folder, file))
        anchor = f"#{anchor}" if anchor else ""
        if resolved in names:
            return link(names[resolved]) + anchor
        if resolved.lower().endswith(IMAGES):
            return RAW + resolved
        return BLOB + resolved + anchor

    text = re.sub(r"\]\(([^)\s]+)\)", lambda m: f"]({target(m.group(1))})", text)
    return re.sub(r'(src|href)="([^"]+)"', lambda m: f'{m.group(1)}="{target(m.group(2))}"', text)


def sidebar(names):
    def entry(path):
        emoji = re.match(r"^# (\S+)", read(path), re.M)
        return f"- {emoji.group(1) if emoji else ''} [{names[path]}]({link(names[path])})"
    lines = [f"### [🏠 Manual](Home)", "", "**Español**", ""]
    lines += [entry(f"{MANUAL}/es/{p}.md") for p in ORDER]
    lines += ["", f"**[English]({link(names[f'{MANUAL}/en/README.md'])})**", ""]
    lines += [entry(f"{MANUAL}/en/{p}.md") for p in ORDER]
    return "\n".join(lines) + "\n"


def footer():
    return (f"Este manual se genera desde [`docs/manual`]({BLOB}{MANUAL}): para corregirlo, cámbielo allí. · "
            f"This guide is generated from [`docs/manual`]({BLOB}{MANUAL}): to fix it, change it there.\n")


def main(out):
    if not os.path.isdir(os.path.join(out, ".git")):
        sys.exit(f"{out} no es un clon de la Wiki (falta .git). Clónela con: git clone https://github.com/{REPO}.wiki.git")
    for old in [f for f in os.listdir(out) if f.endswith(".md")]:
        os.remove(os.path.join(out, old))
    names = pages()
    files = {slug(name) + ".md": convert(path, read(path), names) for path, name in names.items()}
    files["_Sidebar.md"] = sidebar(names)
    files["_Footer.md"] = footer()
    for name, text in files.items():
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
    print(f"{len(files)} archivos en {out}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
