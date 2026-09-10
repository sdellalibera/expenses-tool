import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

import { api } from "../api/client";
import { formatDate, formatMoney } from "../format";
import { useUserId } from "../hooks/useUserId";
import type { Expense, TripSummary } from "../types";

/** `/trips/expenses` — every expense as a table, optionally filtered by trip. */
export default function ExpensesPage() {
  const userId = useUserId();
  const [searchParams, setSearchParams] = useSearchParams();
  const tripId = searchParams.get("tripId") ?? "";

  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [trips, setTrips] = useState<TripSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    Promise.all([api.listExpenses(userId, tripId || undefined), api.listTrips(userId)])
      .then(([expenseList, tripList]) => {
        if (cancelled) return;
        setExpenses(expenseList);
        setTrips(tripList);
        setError(null);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [userId, tripId]);

  const tripNames = useMemo(
    () => new Map(trips.map(({ trip }) => [trip.id, trip.name])),
    [trips],
  );

  const total = useMemo(() => expenses.reduce((sum, expense) => sum + expense.totalAmount, 0), [expenses]);
  const currency = expenses[0]?.currency ?? "EUR";

  return (
    <section aria-label="Expenses">
      <div className="page-head">
        <h1>Expenses</h1>

        <label className="filter">
          <span>Trip</span>
          <select
            value={tripId}
            onChange={(event) => {
              const value = event.target.value;
              setSearchParams(value ? { tripId: value } : {});
            }}
          >
            <option value="">All trips</option>
            {trips.map(({ trip }) => (
              <option key={trip.id} value={trip.id}>
                {trip.name}
              </option>
            ))}
          </select>
        </label>
      </div>

      {loading && <p className="state">Loading expenses…</p>}
      {error && <p className="state state-error">{error}</p>}

      {!loading && !error && expenses.length === 0 && (
        <p className="state">
          No expenses yet. Send a receipt photo in the <Link to="/home">chat</Link>.
        </p>
      )}

      {!loading && !error && expenses.length > 0 && (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Merchant</th>
                  <th scope="col">Trip</th>
                  <th scope="col">Category</th>
                  <th scope="col">Items</th>
                  <th scope="col" className="right">
                    Total
                  </th>
                </tr>
              </thead>
              <tbody>
                {expenses.map((expense) => (
                  <tr key={expense.id}>
                    <td data-label="Date">{formatDate(expense.date)}</td>
                    <td data-label="Merchant">
                      <Link to={`/trips/expenses/${encodeURIComponent(expense.id)}`}>{expense.merchant}</Link>
                    </td>
                    <td data-label="Trip">{tripNames.get(expense.tripId) ?? expense.tripId}</td>
                    <td data-label="Category">
                      <span className="chip">{expense.category}</span>
                    </td>
                    <td data-label="Items">{expense.lineItems?.length ?? 0}</td>
                    <td data-label="Total" className="right amount">
                      {formatMoney(expense.totalAmount, expense.currency)}
                    </td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={5}>{expenses.length} expenses</td>
                  <td className="right amount">{formatMoney(total, currency)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
