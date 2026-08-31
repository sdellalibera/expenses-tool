"""
Expenses Agent Executor.
"""
import datetime
import json
import logging
import os
import uuid
from typing import Any, Optional

from agent_framework import Agent, Content, Message
from agent_framework.foundry import ContentUnderstandingContextProvider, FoundryChatClient
from azure.cosmos.aio import CosmosClient
from azure.identity.aio import DefaultAzureCredential

from tools.parser_tool import translateYAMLtoJSON

logger = logging.getLogger(__name__)


def _cosmos_endpoint() -> str:
    endpoint = os.environ.get("COSMOS_ENDPOINT")
    if endpoint:
        return endpoint
    for part in os.environ.get("ConnectionStrings__cosmos-db", "").split(";"):
        key, _, value = part.partition("=")
        if key.strip().lower() == "accountendpoint" and value:
            return value.strip()
    raise RuntimeError("Cosmos endpoint not configured.")


class ExpensesAgentExecutor:
    def __init__(self):
        self._credential = DefaultAzureCredential()

        self.cu = ContentUnderstandingContextProvider(
            endpoint=os.environ["contentUnderstandingEndpoint"],
            credential=self._credential,
            analyzer_id="ExpensesAnalyzer",
            max_wait=None,
        )

        self.agent: Agent = Agent(
            client=FoundryChatClient(
                project_endpoint=os.environ["FOUNDRY_ENDPOINT"],
                model=os.environ["FOUNDRY_DEPLOYMENT"],
                credential=self._credential,
            ),
            name="expensesagent",
            context_providers=[self.cu],
            tools=[translateYAMLtoJSON],
            instructions="""You are an expenses processing agent.

An Azure Content Understanding analyzer (`ExpensesAnalyzer`) runs on the uploaded receipt and injects a YAML block with the extracted fields (vendor, dates, totals, line items, taxes, etc.) and confidence scores.

Translate that YAML into a single JSON object that preserves every field and confidence score. Output only the JSON object — no prose, no markdown fences, no commentary. It must be valid and directly parseable.
""",)

        self._cosmos: Optional[CosmosClient] = None
        self._records = None

    async def __aenter__(self):
        await self.cu.__aenter__()
        self._cosmos = CosmosClient(_cosmos_endpoint(), credential=self._credential)
        db = self._cosmos.get_database_client(os.environ["COSMOS_DATABASE"])
        self._records = db.get_container_client(os.environ.get("COSMOS_RECORDS_CONTAINER", "records"))
        return self

    async def __aexit__(self, exc_type, exc, tb):
        if self._cosmos is not None:
            await self._cosmos.close()
        await self.cu.__aexit__(exc_type, exc, tb)

    async def report(
        self,
        image_bytes: bytes,
        content_type: Optional[str],
        filename: Optional[str],
        user_id: str,
        note: Optional[str],
        conversation_id: Optional[str],
    ) -> dict[str, Any]:
        response = await self.agent.run(
            Message(
                role="user",
                contents=[
                    Content.from_text(note or "Process this expense receipt."),
                    Content.from_data(
                        image_bytes,
                        content_type or "image/jpeg",
                        additional_properties={"filename": filename or "receipt.jpg"},
                    ),
                ],
            ),
        )

        extraction = json.loads(response.text)
        record_id = str(uuid.uuid4())
        resolved_conversation_id = conversation_id or record_id

        await self._records.upsert_item({
            "id": record_id,
            "userId": user_id,
            "conversationId": resolved_conversation_id,
            "record": extraction,
            "note": note,
            "createdAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        })

        return {
            "conversationId": resolved_conversation_id,
            "recordId": record_id,
            "userId": user_id,
            "result": extraction,
        }
