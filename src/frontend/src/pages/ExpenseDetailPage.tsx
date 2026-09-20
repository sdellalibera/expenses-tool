import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

import { api } from "../api/client";
import { formatDate, formatMoney } from "../format";
import { useUserId } from "../hooks/useUserId";
import type { Expense, Trip } from "../types";

/** `/trips/expenses/:expenseId` — everything stored about one receipt. */
export default function ExpenseDetailPage() {
  const userId = useUserId();
  const { expenseId } = useParams<{ expenseId: string }>();

  const [expense, setExpense] = useState<Expense | null>(null);
  const [trip, setTrip] = useState<Trip | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [photoError, setPhotoError] = useState(false);

  useEffect(() => {
    if (!expenseId) return;
    let cancelled = false;
    setLoading(true);
    setTrip(null);
    setPhotoError(false);

    api
      .getExpense(userId, expenseId)
      .then(async (result) => {
        if (cancelled) return;
        setExpense(result);
        setError(null);
        try {
          const summary = await api.getTrip(userId, result.tripId);
          if (!cancelled) setTrip(summary.trip);
        } catch {
          /* the trip is optional context */
        }
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
  }, [userId, expenseId]);

  if (loading) return <p className="state">Loading expense…</p>;
  if (error) return <p className="state state-error">{error}</p>;
  if (!expense) return <p className="state">Expense not found.</p>;

  const itemsTotal = expense.lineItems.reduce((sum, item) => sum + item.price * (item.quantity || 1), 0);
  const photoUrl = expense.photoUrl ? api.expensePhotoUrl(userId, expense.id) : null;

  return (
    <section aria-label="Expense detail">
      <p className="breadcrumb">
        <Link to="/trips/expenses">← All expenses</Link>
      </p>

      <h1>{expense.merchant}</h1>

      <p className="headline-amount">{formatMoney(expense.totalAmount, expense.currency)}</p>

      <dl className="facts">
        <div>
          <dt>Date</dt>
          <dd>{formatDate(expense.date)}</dd>
        </div>
        <div>
          <dt>Category</dt>
          <dd>
            <span className="chip">{expense.category}</span>
          </dd>
        </div>
        <div>
          <dt>Trip</dt>
          <dd>
            {trip ? (
              <Link to={`/trips/expenses?tripId=${encodeURIComponent(expense.tripId)}`}>{trip.name}</Link>
            ) : (
              expense.tripId
            )}
          </dd>
        </div>
        <div>
          <dt>Receipt</dt>
          <dd>
            {photoUrl ? (
              <a href={photoUrl} target="_blank" rel="noreferrer">
                {expense.sourceImage || "View receipt"}
              </a>
            ) : (
              expense.sourceImage ?? "—"
            )}
          </dd>
        </div>
        <div>
          <dt>Recorded</dt>
          <dd>{formatDate(expense.createdAt)}</dd>
        </div>
        <div>
          <dt>Expense id</dt>
          <dd>
            <code>{expense.id}</code>
          </dd>
        </div>
      </dl>

      {photoUrl && (
        <>
          <h2>Receipt image</h2>
          {photoError ? (
            <p className="state" role="status">
              Receipt preview could not be loaded. Try opening the receipt link above.
            </p>
          ) : (
            <a href={photoUrl} target="_blank" rel="noreferrer">
              <img
                className="receipt-photo"
                src={photoUrl}
                alt={`Receipt from ${expense.merchant}`}
                loading="lazy"
                onError={() => setPhotoError(true)}
              />
            </a>
          )}
        </>
      )}

      {expense.notes && (
        <>
          <h2>Note</h2>
          <p>{expense.notes}</p>
        </>
      )}

      <h2>Line items</h2>
      {expense.lineItems.length === 0 ? (
        <p className="state">No line items were extracted from this receipt.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Description</th>
                <th scope="col">Category</th>
                <th scope="col" className="right">
                  Qty
                </th>
                <th scope="col" className="right">
                  Price
                </th>
              </tr>
            </thead>
            <tbody>
              {expense.lineItems.map((item, index) => (
                <tr key={`${item.description}-${index}`}>
                  <td data-label="Description">{item.description}</td>
                  <td data-label="Category">
                    <span className="chip">{item.category}</span>
                  </td>
                  <td data-label="Qty" className="right">
                    {item.quantity}
                  </td>
                  <td data-label="Price" className="right amount">
                    {formatMoney(item.price, expense.currency)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={3}>Line items total</td>
                <td className="right amount">{formatMoney(itemsTotal, expense.currency)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
    </section>
  );
}
