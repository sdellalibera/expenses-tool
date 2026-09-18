"""Tests for the YAML -> JSON tool the agent uses on Content Understanding output."""

from __future__ import annotations

import json

from expenses_agent.tools.parser_tool import translateYAMLtoJSON

RECEIPT_YAML = """
Name: Hofbrauhaus
Category: food
TotalAmount: 42.5
Date: 2026-03-02
LineItems:
  - Title: Schnitzel
    Price: 24.5
  - Title: Weissbier
    Price: 18.0
"""


def _invoke(yaml_text: str) -> str:
    # `@tool` wraps the function; the original stays reachable for direct calls.
    func = getattr(translateYAMLtoJSON, "func", None) or getattr(
        translateYAMLtoJSON, "__wrapped__", translateYAMLtoJSON
    )
    return func(yaml_text)


def test_translates_receipt_yaml_into_json():
    result = json.loads(_invoke(RECEIPT_YAML))

    assert result["Name"] == "Hofbrauhaus"
    assert result["Category"] == "food"
    assert result["TotalAmount"] == 42.5
    assert [item["Title"] for item in result["LineItems"]] == ["Schnitzel", "Weissbier"]


def test_dates_do_not_crash_the_tool():
    # yaml.safe_load turns `2026-03-02` into a datetime.date, which plain
    # json.dumps cannot encode.
    assert json.loads(_invoke(RECEIPT_YAML))["Date"] == "2026-03-02"


def test_empty_yaml_returns_null():
    assert json.loads(_invoke("")) is None


def test_invalid_yaml_reports_an_error_instead_of_raising():
    result = json.loads(_invoke("a:\n  - b\n - c\n"))

    assert "error" in result


def test_unicode_is_preserved():
    result = json.loads(_invoke("Name: Café Größe"))

    assert result["Name"] == "Café Größe"


def test_tool_is_registered_with_a_stable_name():
    assert getattr(translateYAMLtoJSON, "name", None) == "TranslateYAMLToJSON"
