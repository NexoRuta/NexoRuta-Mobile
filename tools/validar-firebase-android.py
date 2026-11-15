#!/usr/bin/env python3
"""Validate that an Android Firebase client belongs to the backend's Firebase project."""

import argparse
import json
import re
import sys
from pathlib import Path


def validar(config_path: Path, project_id: str, package_name: str) -> None:
    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        raise ValueError("Firebase Android configuration is missing or invalid JSON.") from None

    if not isinstance(config, dict):
        raise ValueError("Firebase Android configuration must be a JSON object.")
    project = config.get("project_info")
    if not isinstance(project, dict) or project.get("project_id") != project_id:
        raise ValueError("Firebase Android configuration does not match the expected backend project.")

    project_number = project.get("project_number")
    if not isinstance(project_number, str) or not project_number.isdigit():
        raise ValueError("Firebase Android configuration has no valid project number.")

    clients = config.get("client")
    if not isinstance(clients, list):
        raise ValueError("Firebase Android configuration has no Android clients.")

    matching = []
    for client in clients:
        if not isinstance(client, dict):
            continue
        client_info = client.get("client_info")
        android_info = client_info.get("android_client_info") if isinstance(client_info, dict) else None
        if isinstance(android_info, dict) and android_info.get("package_name") == package_name:
            matching.append(client)
    if len(matching) != 1:
        raise ValueError(f"Firebase configuration must contain exactly one Android client for {package_name}.")

    client_info = matching[0].get("client_info")
    app_id = client_info.get("mobilesdk_app_id", "") if isinstance(client_info, dict) else ""
    app_id_match = re.fullmatch(r"1:(\d+):android:[A-Za-z0-9_-]+", app_id) if isinstance(app_id, str) else None
    if app_id_match is None or app_id_match.group(1) != project_number:
        raise ValueError("Firebase Android app ID does not belong to the configured project number.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--project-id", required=True)
    parser.add_argument("--package", default="uy.nexoruta.repartidor")
    args = parser.parse_args()

    try:
        validar(args.config, args.project_id, args.package)
    except ValueError as error:
        print(str(error), file=sys.stderr)
        return 2

    print(f"Firebase Android configuration validated for {args.package} in project {args.project_id}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
