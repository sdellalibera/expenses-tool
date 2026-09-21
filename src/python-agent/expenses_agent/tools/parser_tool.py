import json

import yaml

def receipt_json(rendered: str, source: str) -> str:
    lines = rendered.strip().splitlines()
    if not lines or lines[0] != "---":
        raise ValueError("Receipt analysis is missing structured fields.")
    try:
        end = lines.index("---", 1)
        payload = yaml.safe_load("\n".join(lines[1:end]))
    except (ValueError, yaml.YAMLError) as exc:
        raise ValueError("Receipt analysis contains invalid structured fields.") from exc
    if not isinstance(payload, dict) or not isinstance(payload.get("fields"), dict) or not payload["fields"]:
        raise ValueError("Receipt analysis returned no usable fields.")
    metadata = payload.get("customMetadata") or {}
    if not isinstance(metadata, dict) or metadata.get("source") != source:
        raise ValueError("Receipt analysis source does not match the uploaded receipt.")
    return json.dumps({"source": source, "fields": payload["fields"]}, ensure_ascii=False, separators=(",", ":"), default=str)
