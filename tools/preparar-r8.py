#!/usr/bin/env python3
"""Fetch the R8 release required by Kotlin 2.4 metadata and verify its digest."""
from hashlib import sha256
from pathlib import Path
from urllib.request import urlopen

VERSION = "9.1.31"
EXPECTED_SHA256 = "3b4de3053885da105e39c15212261d22653d6d1b5eb92323dd04ae913cc8286f"
URL = f"https://dl.google.com/dl/android/maven2/com/android/tools/r8/{VERSION}/r8-{VERSION}.jar"
DESTINO = Path(__file__).resolve().parents[2] / ".local" / "mobile" / "toolchain" / f"r8-{VERSION}.jar"


def main() -> None:
    DESTINO.parent.mkdir(parents=True, exist_ok=True)
    if not DESTINO.exists() or sha256(DESTINO.read_bytes()).hexdigest() != EXPECTED_SHA256:
        parcial = DESTINO.with_suffix(".jar.partial")
        try:
            with urlopen(URL, timeout=60) as respuesta, parcial.open("wb") as archivo:
                while bloque := respuesta.read(1024 * 1024):
                    archivo.write(bloque)
            if sha256(parcial.read_bytes()).hexdigest() != EXPECTED_SHA256:
                raise RuntimeError("El SHA-256 del archivo R8 descargado no coincide.")
            parcial.replace(DESTINO)
        finally:
            parcial.unlink(missing_ok=True)
    print(DESTINO)


if __name__ == "__main__":
    main()
