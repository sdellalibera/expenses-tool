"""Package the canonical analyzer, including when rebuilding from an sdist."""

from pathlib import Path

from hatchling.builders.hooks.plugin.interface import BuildHookInterface


class CustomBuildHook(BuildHookInterface):
    def initialize(self, version, build_data):
        root = Path(self.root)
        definition = root.parent / "analyzers" / "ExpensesAnalyzer.json"
        if not definition.is_file():
            definition = root / "expenses_agent" / "ExpensesAnalyzer.json"
        if not definition.is_file():
            raise FileNotFoundError("The canonical ExpensesAnalyzer.json definition is missing.")
        build_data["force_include"][str(definition)] = "expenses_agent/ExpensesAnalyzer.json"
