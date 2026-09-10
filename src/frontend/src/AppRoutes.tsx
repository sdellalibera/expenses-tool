import { Navigate, Route, Routes } from "react-router-dom";

import App from "./App";
import ChatPage from "./pages/ChatPage";
import ExpenseDetailPage from "./pages/ExpenseDetailPage";
import ExpensesPage from "./pages/ExpensesPage";
import TripsPage from "./pages/TripsPage";

/**
 * Route table.
 *
 * `/home`                      – chat with the agent (camera + upload)
 * `/trips`                     – every work trip with its expense roll-up
 * `/trips/expenses`            – all expenses as a table
 * `/trips/expenses/:expenseId` – one expense in detail
 */
export default function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<App />}>
        <Route index element={<Navigate to="/home" replace />} />
        <Route path="home" element={<ChatPage />} />
        <Route path="trips" element={<TripsPage />} />
        <Route path="trips/expenses" element={<ExpensesPage />} />
        <Route path="trips/expenses/:expenseId" element={<ExpenseDetailPage />} />
        <Route path="*" element={<Navigate to="/home" replace />} />
      </Route>
    </Routes>
  );
}
