"""Agent tools that do not touch the database.

Record CRUD lives in the C# MCP server; this package only holds local helpers.
"""

from .parser_tool import translateYAMLtoJSON, translate_yaml_to_json

__all__ = ["translateYAMLtoJSON", "translate_yaml_to_json"]
