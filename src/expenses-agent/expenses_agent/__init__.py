"""Expenses agent (Python / Microsoft Agent Framework).

A Foundry-hosted-target agent that:
  * analyzes receipt/invoice images with Azure AI Content Understanding
    (via a context provider), and
  * uses two MCP servers as tools — the SQL MCP Server (Data API builder) to
    read/write structured expense records, and the Storage MCP Server to save
    and read receipt images.
"""

from .config import AgentSettings, load_settings

__all__ = ["AgentSettings", "load_settings"]
