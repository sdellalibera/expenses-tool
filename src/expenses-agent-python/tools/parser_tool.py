from agent_framework import tool
from typing import Annotated,Any
from pydantic import Field
import yaml
import json


@tool(name="TranslateYAMLToJSON",description="helps an agent translate yaml content into json object")
def translateYAMLtoJSON( 
    yamlString:Annotated[str,Field(description="yaml body content that has to be translated into a JSON object")]
)-> str:
    parsed_yaml = yaml.safe_load(yamlString)
    return json.dumps(parsed_yaml,ensure_ascii=False,indent=2)