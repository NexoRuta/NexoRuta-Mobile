import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "validar-firebase-android.py"
PROJECT_ID = "nexoruta-mobile"


def configuracion(package="uy.nexoruta.repartidor", app_project_number="123456"):
    return {
        "project_info": {"project_id": PROJECT_ID, "project_number": "123456"},
        "client": [
            {
                "client_info": {
                    "mobilesdk_app_id": f"1:{app_project_number}:android:abcdef0123456789",
                    "android_client_info": {"package_name": package},
                }
            }
        ],
    }


class ValidarFirebaseAndroidTests(unittest.TestCase):
    def ejecutar(self, contenido, project_id=PROJECT_ID):
        with tempfile.TemporaryDirectory() as directorio:
            archivo = Path(directorio) / "google-services.json"
            archivo.write_text(contenido if isinstance(contenido, str) else json.dumps(contenido), encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(SCRIPT), "--config", str(archivo), "--project-id", project_id],
                capture_output=True,
                text=True,
                check=False,
            )

    def test_acepta_proyecto_y_paquete_android_correctos(self):
        resultado = self.ejecutar(configuracion())
        self.assertEqual(0, resultado.returncode, resultado.stderr)
        self.assertIn("uy.nexoruta.repartidor", resultado.stdout)

    def test_rechaza_proyecto_distinto_al_worker(self):
        resultado = self.ejecutar(configuracion(), "otro-proyecto")
        self.assertEqual(2, resultado.returncode)
        self.assertIn("expected backend project", resultado.stderr)

    def test_rechaza_paquete_android_distinto(self):
        resultado = self.ejecutar(configuracion(package="uy.otro.paquete"))
        self.assertEqual(2, resultado.returncode)
        self.assertIn("uy.nexoruta.repartidor", resultado.stderr)

    def test_rechaza_app_id_de_otro_numero_de_proyecto(self):
        resultado = self.ejecutar(configuracion(app_project_number="999999"))
        self.assertEqual(2, resultado.returncode)
        self.assertIn("project number", resultado.stderr)

    def test_rechaza_json_malformado(self):
        resultado = self.ejecutar("{")
        self.assertEqual(2, resultado.returncode)
        self.assertIn("invalid JSON", resultado.stderr)

    def test_rechaza_json_que_no_es_objeto(self):
        resultado = self.ejecutar("[]")
        self.assertEqual(2, resultado.returncode)
        self.assertIn("JSON object", resultado.stderr)


if __name__ == "__main__":
    unittest.main()
