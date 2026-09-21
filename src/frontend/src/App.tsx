import { NavLink, Outlet } from "react-router-dom";

import { ChatSessionProvider } from "./hooks/useChatSession";

/** Shell around every page: brand, primary navigation and the routed content. */
export default function App() {
  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            €
          </span>
          <div>
            <strong>Expenses</strong>
            <span className="brand-sub">Snap a receipt, the agent files it.</span>
          </div>
        </div>

        <nav className="app-nav" aria-label="Main">
          <NavLink to="/home">Chat</NavLink>
          <NavLink to="/trips" end>
            Trips
          </NavLink>
          <NavLink to="/trips/expenses" end>
            Expenses
          </NavLink>
        </nav>
      </header>

      <main className="app-main">
        <ChatSessionProvider>
          <Outlet />
        </ChatSessionProvider>
      </main>
    </div>
  );
}
