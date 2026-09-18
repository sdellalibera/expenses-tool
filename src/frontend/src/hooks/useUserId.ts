const USER_ID_STORAGE_KEY = "expenses.userId";

/**
 * The demo has no sign-in: a stable random id is generated once per browser and
 * used as the Cosmos partition key for that user's trips and expenses.
 */
export function getOrCreateUserId(): string {
  if (typeof window === "undefined") return "anonymous";

  try {
    const existing = window.localStorage.getItem(USER_ID_STORAGE_KEY);
    if (existing) return existing;

    const fresh =
      typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID()
        : `user-${Math.random().toString(36).slice(2, 10)}`;

    window.localStorage.setItem(USER_ID_STORAGE_KEY, fresh);
    return fresh;
  } catch {
    return "anonymous";
  }
}

export function useUserId(): string {
  return getOrCreateUserId();
}
