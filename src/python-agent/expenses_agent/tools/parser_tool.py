from agent_framework import tool
from typing import Annotated
from pydantic import Field
import yaml
import json


@tool(name="TranslateYAMLToJSON", description="helps an agent translate yaml content into json object")
def translateYAMLtoJSON(
    yamlString: Annotated[str, Field(description="yaml body content that has to be translated into a JSON object")]
) -> str:
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
