import yaml
import json


def translateYAMLtoJSON(yamlString: str) -> str:
    try:
        parsed_yaml = yaml.safe_load(yamlString)
    except yaml.YAMLError as exc:
        # A malformed block must not abort the whole chat turn.
        return json.dumps({"error": f"Invalid YAML: {exc}"}, ensure_ascii=False)

    # `default=str` because Content Understanding emits real dates (2026-03-02),
    # which yaml.safe_load turns into datetime.date and json.dumps cannot encode.
    return json.dumps(parsed_yaml, ensure_ascii=False, indent=2, default=str)


# Snake-case alias used by the newer modules.
translate_yaml_to_json = translateYAMLtoJSON


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
