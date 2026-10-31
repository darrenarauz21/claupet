"""Genera los paquetes de Claupet para Windows, Linux y macOS en paquetes/.

Los nombres no llevan versión, para que los enlaces del README a la última Release
(releases/latest/download/<archivo>) sigan sirviendo en cada versión nueva:
Claupet-windows-x64.exe, Claupet-linux-x64.tar.gz, Claupet-macos-arm64.zip y
Claupet-macos-x64.zip.

    python herramientas/empaquetar.py            los tres sistemas
    python herramientas/empaquetar.py linux mac  solo los indicados
    python herramientas/empaquetar.py --clave <ruta/clave.pem>
                                                 además, firma cada paquete (paquete.firma)

Con --clave, junto a cada paquete queda su firma (<paquete>.firma), que se sube a la Release con él: la app
la comprueba antes de instalar una versión nueva. Firma la misma app («Claupet --release-sign», ES256 como
los paquetes de extensiones); la contraseña de la clave, si tiene, va en CLAUPET_KEY_PASSWORD. La clave
privada nunca va en el repositorio: la app solo lleva la pública (Updates.ReleaseKey en Core/Updates.cs).

Solo usa la biblioteca estándar. Arma los .tar.gz y .zip a mano para conservar los permisos de
ejecución, que se pierden al comprimir desde Windows.
"""
import io
import os
import shutil
import struct
import subprocess
import sys
import tarfile
import zipfile
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "paquetes")
WORK = os.path.join(OUT, "_trabajo")
AVALONIA = os.path.join(ROOT, "Claupet.Avalonia", "Claupet.Avalonia.csproj")
VERSION = "2.4.1"

# ── Íconos: el de Claupet, el mismo de app.ico y de Core/ClaupetLogo.cs, en pixeles ───────────

BODY, FACE, EYE, BASE = (0xD9, 0x77, 0x57, 255), (0xF4, 0xE3, 0xD3, 255), (0x1F, 0x1E, 0x1D, 255), (0xB4, 0x53, 0x2F, 255)
# Los de después van encima: la cara sobre el cuerpo y los ojos sobre la cara.
LOGO = (9, 9, [(BODY, 1, 1, 7, 6), (BODY, 0, 2, 1, 3), (BODY, 8, 2, 1, 3), (FACE, 2, 3, 5, 3),
               (EYE, 3, 4, 1, 1), (EYE, 5, 4, 1, 1), (BASE, 2, 7, 5, 2)])


def render(size):
    columns, rows, rects = LOGO
    unit = max(1, size // columns)
    x0, y0 = (size - columns * unit) // 2, (size - rows * unit) // 2
    pixels = [[(0, 0, 0, 0)] * size for _ in range(size)]
    for color, x, y, w, h in rects:
        for py in range(y0 + y * unit, y0 + (y + h) * unit):
            for px in range(x0 + x * unit, x0 + (x + w) * unit):
                pixels[py][px] = color
    return pixels


def png(size):
    pixels = render(size)
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in pixels)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def icns():
    # Tipos de ícono de macOS con contenido PNG.
    entries = [(b"icp4", 16), (b"icp5", 32), (b"icp6", 64), (b"ic07", 128), (b"ic08", 256),
               (b"ic09", 512), (b"ic10", 1024), (b"ic11", 32), (b"ic12", 64), (b"ic13", 256), (b"ic14", 512)]
    body = b"".join(kind + struct.pack(">I", 8 + len(data)) + data for kind, data in ((k, png(s)) for k, s in entries))
    return b"icns" + struct.pack(">I", 8 + len(body)) + body


# ── Compilación ──────────────────────────────────────────────────────────────────────────────

def publish(project, rid, output, single_file):
    args = ["dotnet", "publish", project, "-c", "Release", "-r", rid, "--self-contained", "true", "-o", output, "-nologo", "-v", "q"]
    if single_file:
        args += ["-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true"]
    print(f"  compilando {rid}…")
    subprocess.run(args, check=True)
    for pdb in [f for f in os.listdir(output) if f.endswith(".pdb")]:
        os.remove(os.path.join(output, pdb))


def text(name):
    """Archivo de herramientas/ con saltos de línea de Unix (los scripts fallan con CR)."""
    with open(os.path.join(ROOT, "herramientas", name), encoding="utf-8", newline="") as h:
        return h.read().replace("\r\n", "\n")


def add_tar(tar, name, data, mode):
    info = tarfile.TarInfo(name)
    info.size, info.mode = len(data), mode
    tar.addfile(info, io.BytesIO(data))


def add_zip(archive, name, data, mode):
    info = zipfile.ZipInfo(name)
    info.create_system = 3  # Unix: así se conservan los permisos
    info.external_attr = (mode | 0o100000) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(info, data)


def windows():
    staging = os.path.join(WORK, "win-x64")
    shutil.rmtree(staging, ignore_errors=True)
    publish(AVALONIA, "win-x64", staging, single_file=True)
    shutil.copyfile(os.path.join(staging, "Claupet.exe"), os.path.join(OUT, "Claupet-windows-x64.exe"))
    print("  listo: paquetes/Claupet-windows-x64.exe")


def linux():
    staging = os.path.join(WORK, "linux-x64")
    shutil.rmtree(staging, ignore_errors=True)
    publish(AVALONIA, "linux-x64", staging, single_file=True)
    name = "Claupet-linux-x64"
    path = os.path.join(OUT, name + ".tar.gz")
    with tarfile.open(path, "w:gz") as tar:
        with open(os.path.join(staging, "Claupet"), "rb") as h:
            add_tar(tar, f"{name}/Claupet", h.read(), 0o755)
        add_tar(tar, f"{name}/instalar.sh", text("instalar-linux.sh").encode(), 0o755)
        add_tar(tar, f"{name}/claupet.png", png(256), 0o644)
        add_tar(tar, f"{name}/LEEME.txt", text("LEEME-linux.txt").encode(), 0o644)
    print(f"  listo: paquetes/{name}.tar.gz")


def foreign(rel):
    """Lo de otros sistemas que la voz deja en la publicación de macOS: Whisper copia las bibliotecas de todos
    (runtimes/<sistema>/) y ONNX Runtime, las suyas de Windows."""
    parts = rel.split("/")
    if parts[0] == "runtimes" and len(parts) > 2 and not parts[1].startswith(("macos", "osx")):
        return True
    return rel in ("onnxruntime.dll", "onnxruntime_providers_shared.dll")


def mac(rid):
    staging = os.path.join(WORK, rid)
    shutil.rmtree(staging, ignore_errors=True)
    # En macOS no es un solo archivo: así codesign firma cada biblioteca dentro del .app.
    publish(AVALONIA, rid, staging, single_file=False)
    app = "Claupet.app"
    name = f"Claupet-macos-{rid.split('-')[1]}"
    plist = text("Info.plist").replace("{VERSION}", VERSION)
    path = os.path.join(OUT, name + ".zip")
    with zipfile.ZipFile(path, "w") as archive:
        for folder, _, files in os.walk(staging):
            for file in files:
                full = os.path.join(folder, file)
                rel = os.path.relpath(full, staging).replace("\\", "/")
                if foreign(rel):
                    continue
                with open(full, "rb") as h:
                    data = h.read()
                executable = file in ("Claupet", "createdump") or data[:4] in (b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe")
                add_zip(archive, f"{name}/{app}/Contents/MacOS/{rel}", data, 0o755 if executable else 0o644)
        add_zip(archive, f"{name}/{app}/Contents/Info.plist", plist.encode(), 0o644)
        add_zip(archive, f"{name}/{app}/Contents/Resources/app.icns", icns(), 0o644)
        add_zip(archive, f"{name}/preparar-mac.sh", text("preparar-mac.sh").encode(), 0o755)
        add_zip(archive, f"{name}/LEEME.txt", text("LEEME-mac.txt").encode(), 0o644)
    print(f"  listo: paquetes/{name}.zip")


def sign(key, files):
    """La firma de cada paquete (<paquete>.firma), con la app misma: Python no trae ECDSA en su biblioteca estándar."""
    args = ["dotnet", "run", "--project", AVALONIA, "-c", "Release", "-nologo", "-v", "q", "--",
            "--release-sign", os.path.abspath(key), VERSION] + files
    print("  firmando…")
    subprocess.run(args, check=True)


if __name__ == "__main__":
    args = sys.argv[1:]
    key = None
    if "--clave" in args:
        at = args.index("--clave")
        if at + 1 >= len(args):
            sys.exit("Falta la ruta de la clave: --clave <ruta/clave.pem>")
        key = args[at + 1]
        del args[at:at + 2]
        if not os.path.isfile(key):
            sys.exit(f"No existe la clave: {key}")
    wanted = set(args) or {"windows", "linux", "mac"}
    os.makedirs(OUT, exist_ok=True)
    built = []
    if "windows" in wanted:
        print("Windows"); windows(); built.append("Claupet-windows-x64.exe")
    if "linux" in wanted:
        print("Linux"); linux(); built.append("Claupet-linux-x64.tar.gz")
    if "mac" in wanted:
        print("macOS"); mac("osx-arm64"); mac("osx-x64"); built += ["Claupet-macos-arm64.zip", "Claupet-macos-x64.zip"]
    shutil.rmtree(WORK, ignore_errors=True)
    if key:
        print("Firmas"); sign(key, [os.path.join(OUT, name) for name in built])
