import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

import { api } from "../api/client";
import { formatDateRange, formatMoney } from "../format";
import { useUserId } from "../hooks/useUserId";
import type { TripSummary } from "../types";

/** `/trips` — every work trip with the number of expenses and the total booked. */
export default function TripsPage() {
  const userId = useUserId();
  const [trips, setTrips] = useState<TripSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    api
      .listTrips(userId)
      .then((result) => {
        if (!cancelled) setTrips(result);
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
  }, [userId]);

  if (loading) return <p className="state">Loading trips…</p>;
  if (error) return <p className="state state-error">{error}</p>;

  if (trips.length === 0) {
    return (
      <div className="state">
        <p>No trips yet.</p>
        <p>
          Go to the <Link to="/home">chat</Link> and ask the agent to create one, for example{" "}
          <em>“Create a trip to Munich from 1 to 5 March”</em>.
        </p>
      </div>
    );
  }

  return (
    <section aria-label="Trips">
      <h1>Trips</h1>

      <div className="cards">
        {trips.map(({ trip, expenseCount, totalAmount }) => (
          <Link
            key={trip.id}
            className="card"
            to={`/trips/expenses?tripId=${encodeURIComponent(trip.id)}`}
            aria-label={`${trip.name}, ${expenseCount} expenses`}
          >
            <header>
              <h2>{trip.name}</h2>
              <span className={`chip chip-${trip.status}`}>{trip.status}</span>
            </header>

            <dl>
              <div>
                <dt>Destination</dt>
                <dd>{trip.destination ?? "—"}</dd>
              </div>
              <div>
                <dt>Dates</dt>
                <dd>{formatDateRange(trip.startDate, trip.endDate)}</dd>
              </div>
              <div>
                <dt>Expenses</dt>
                <dd>{expenseCount}</dd>
              </div>
              <div>
                <dt>Total</dt>
                <dd className="amount">{formatMoney(totalAmount, trip.currency ?? "EUR")}</dd>
              </div>
            </dl>
          </Link>
        ))}
      </div>
    </section>
  );
}
