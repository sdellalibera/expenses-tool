import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import AppRoutes from "../AppRoutes";
import type { Expense, TripSummary } from "../types";

const trip = (overrides: Partial<TripSummary["trip"]> = {}): TripSummary["trip"] => ({
  id: "trip-munich",
  userId: "test-user",
  name: "Munich kickoff",
  destination: "Munich",
  startDate: "2026-03-01",
  endDate: "2026-03-05",
  purpose: null,
  currency: "EUR",
  status: "open",
  createdAt: "2026-03-01T08:00:00Z",
  updatedAt: "2026-03-01T08:00:00Z",
  ...overrides,
});

const expense = (overrides: Partial<Expense> = {}): Expense => ({
  id: "exp-1",
  userId: "test-user",
  tripId: "trip-munich",
  merchant: "Hofbrauhaus",
  category: "food",
  date: "2026-03-02",
  totalAmount: 42.5,
  currency: "EUR",
  lineItems: [
    { description: "Schnitzel", category: "food", price: 24.5, quantity: 1 },
    { description: "Weissbier", category: "drink", price: 9, quantity: 2 },
  ],
  notes: "Team dinner",
  sourceImage: "receipt.jpg",
  conversationId: "conv-1",
  createdAt: "2026-03-02T20:00:00Z",
  updatedAt: "2026-03-02T20:00:00Z",
  ...overrides,
});

function mockApi(routes: Record<string, unknown>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const match = Object.keys(routes).find((key) => url.startsWith(key));

    if (!match) {
      return new Response(JSON.stringify({ detail: `no mock for ${url}` }), { status: 404 });
    }
    return new Response(JSON.stringify(routes[match]), {
      status: 200,
      headers: { "content-type": "application/json" },
    });
  });

  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** FormData of the n-th recorded fetch call. */
function formDataOf(fetchMock: ReturnType<typeof mockApi>, index: number): FormData {
  return fetchMock.mock.calls[index][1]?.body as FormData;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
    </MemoryRouter>,
  );
}

describe("routing", () => {
  beforeEach(() => {
    mockApi({ "/api/trips": [], "/api/expenses": [] });
  });

  it("redirects the root to /home", async () => {
    renderAt("/");

    expect(await screen.findByRole("textbox", { name: /message/i })).toBeInTheDocument();
  });

  it("renders the chat page at /home", async () => {
    renderAt("/home");

    expect(await screen.findByRole("button", { name: /send/i })).toBeInTheDocument();
  });

  it("renders unknown routes as the chat page", async () => {
    renderAt("/nope");

    expect(await screen.findByRole("button", { name: /send/i })).toBeInTheDocument();
  });
});

describe("/trips", () => {
  it("lists trips with their expense roll-up", async () => {
    mockApi({
      "/api/trips": [{ trip: trip(), expenseCount: 2, totalAmount: 252.5 }] satisfies TripSummary[],
    });

    renderAt("/trips");

    expect(await screen.findByRole("heading", { name: "Munich kickoff" })).toBeInTheDocument();
    expect(screen.getByText("Munich")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText(/252\.50/)).toBeInTheDocument();
  });

  it("explains what to do when there are no trips", async () => {
    mockApi({ "/api/trips": [] });

    renderAt("/trips");

    expect(await screen.findByText(/No trips yet/i)).toBeInTheDocument();
  });

  it("shows an error when the agent is unreachable", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ detail: "boom" }), { status: 502 })),
    );

    renderAt("/trips");

    expect(await screen.findByText("boom")).toBeInTheDocument();
  });
});

describe("/trips/expenses", () => {
  it("renders the expenses table with a total", async () => {
    mockApi({
      "/api/expenses": [expense(), expense({ id: "exp-2", merchant: "Hotel Bayern", totalAmount: 210 })],
      "/api/trips": [{ trip: trip(), expenseCount: 2, totalAmount: 252.5 }],
    });

    renderAt("/trips/expenses");

    expect(await screen.findByRole("link", { name: "Hofbrauhaus" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Hotel Bayern" })).toBeInTheDocument();

    const table = screen.getByRole("table");
    expect(within(table).getByText("2 expenses")).toBeInTheDocument();
    expect(within(table).getAllByText(/252\.50/).length).toBeGreaterThan(0);
  });

  it("passes the tripId filter to the API", async () => {
    const fetchMock = mockApi({
      "/api/expenses": [expense()],
      "/api/trips": [{ trip: trip(), expenseCount: 1, totalAmount: 42.5 }],
    });

    renderAt("/trips/expenses?tripId=trip-munich");

    await screen.findByRole("link", { name: "Hofbrauhaus" });

    const calls = fetchMock.mock.calls.map(([url]) => String(url));
    expect(calls.some((url) => url.includes("/api/expenses?userId=test-user&tripId=trip-munich"))).toBe(true);
  });

  it("tells the user where to add expenses when the list is empty", async () => {
    mockApi({ "/api/expenses": [], "/api/trips": [] });

    renderAt("/trips/expenses");

    expect(await screen.findByText(/No expenses yet/i)).toBeInTheDocument();
  });
});

describe("/trips/expenses/:expenseId", () => {
  it("shows the merchant, total and line items", async () => {
    mockApi({
      "/api/expenses/exp-1": expense(),
      "/api/trips/trip-munich": { trip: trip(), expenseCount: 1, totalAmount: 42.5 },
    });

    const { container } = renderAt("/trips/expenses/exp-1");

    expect(await screen.findByRole("heading", { name: "Hofbrauhaus", level: 1 })).toBeInTheDocument();
    expect(container.querySelector(".headline-amount")).toHaveTextContent("42.50");
    expect(screen.getByText("Schnitzel")).toBeInTheDocument();
    expect(screen.getByText("Weissbier")).toBeInTheDocument();
    expect(screen.getByText("Team dinner")).toBeInTheDocument();
    expect(screen.getByText("receipt.jpg")).toBeInTheDocument();
  });

  it("reports a missing expense", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ detail: "Expense 'nope' was not found." }), { status: 404 })),
    );

    renderAt("/trips/expenses/nope");

    expect(await screen.findByText(/was not found/i)).toBeInTheDocument();
  });

  it("still renders when the expense has no line items", async () => {
    mockApi({
      "/api/expenses/exp-1": expense({ lineItems: [], notes: null }),
      "/api/trips/trip-munich": { trip: trip(), expenseCount: 1, totalAmount: 42.5 },
    });

    renderAt("/trips/expenses/exp-1");

    expect(await screen.findByText(/No line items were extracted/i)).toBeInTheDocument();
  });
});

describe("chat", () => {
  it("sends the message and renders the agent reply", async () => {
    const user = userEvent.setup();
    const fetchMock = mockApi({
      "/chat": {
        conversationId: "conv-9",
        userId: "test-user",
        reply: "Created the Munich trip.",
        toolCalls: ["create_trip"],
        attachments: [],
      },
    });

    renderAt("/home");

    await user.type(screen.getByRole("textbox", { name: /message/i }), "Create a trip to Munich");
    await user.click(screen.getByRole("button", { name: /send/i }));

    expect(await screen.findByText("Created the Munich trip.")).toBeInTheDocument();
    expect(screen.getByText("create_trip")).toBeInTheDocument();

    const body = formDataOf(fetchMock, 0);
    expect(body.get("userId")).toBe("test-user");
    expect(body.get("message")).toBe("Create a trip to Munich");
  });

  it("reuses the conversation id on the next turn", async () => {
    const user = userEvent.setup();
    const fetchMock = mockApi({
      "/chat": { conversationId: "conv-9", userId: "test-user", reply: "ok", toolCalls: [], attachments: [] },
    });

    renderAt("/home");

    const input = screen.getByRole("textbox", { name: /message/i });
    await user.type(input, "first");
    await user.click(screen.getByRole("button", { name: /send/i }));
    await screen.findByText("ok");

    await user.type(input, "second");
    await user.click(screen.getByRole("button", { name: /send/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    const secondBody = formDataOf(fetchMock, 1);
    expect(secondBody.get("conversationId")).toBe("conv-9");
  });

  it("uploads a receipt photo", async () => {
    const user = userEvent.setup();
    const fetchMock = mockApi({
      "/chat": {
        conversationId: "conv-9",
        userId: "test-user",
        reply: "Stored 42.50 EUR at Hofbrauhaus.",
        toolCalls: ["create_expense"],
        attachments: ["receipt.jpg"],
      },
    });

    renderAt("/home");

    const file = new File(["fake"], "receipt.jpg", { type: "image/jpeg" });
    await user.upload(screen.getByTestId("file-input"), file);

    expect(await screen.findByAltText("receipt.jpg")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /send/i }));

    expect(await screen.findByText("Stored 42.50 EUR at Hofbrauhaus.")).toBeInTheDocument();

    const body = formDataOf(fetchMock, 0);
    expect((body.get("images") as File).name).toBe("receipt.jpg");
  });

  it("shows the agent error instead of failing silently", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ detail: "Foundry is not configured." }), { status: 503 })),
    );

    renderAt("/home");

    await user.type(screen.getByRole("textbox", { name: /message/i }), "hi");
    await user.click(screen.getByRole("button", { name: /send/i }));

    expect(await screen.findByText(/Foundry is not configured/)).toBeInTheDocument();
  });

  it("does not send an empty message", async () => {
    const fetchMock = mockApi({ "/chat": {} });

    renderAt("/home");

    expect(screen.getByRole("button", { name: /send/i })).toBeDisabled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
