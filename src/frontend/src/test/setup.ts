import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vitest";

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem("expenses.userId", "test-user");
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

// jsdom implements neither of these, and the chat page uses both.
if (!URL.createObjectURL) {
  URL.createObjectURL = vi.fn(() => "blob:mock");
  URL.revokeObjectURL = vi.fn();
}

Element.prototype.scrollTo = Element.prototype.scrollTo ?? vi.fn();
