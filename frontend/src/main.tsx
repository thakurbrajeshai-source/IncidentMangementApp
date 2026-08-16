import React from "react";
import ReactDOM from "react-dom/client";
import { createBrowserRouter, RouterProvider, Navigate } from "react-router-dom";
import { Login } from "./pages/Login";
import { Reporter } from "./pages/Reporter";
import { Resolver } from "./pages/Resolver";
import { Admin } from "./pages/Admin";
import { Workflows } from "./pages/Workflows";
import { IncidentDetail } from "./pages/IncidentDetail";
import { tokenStore, type User } from "./api";
import "./styles.css";

function RoleGate({ allow, children }: { allow: User["role"][]; children: React.ReactNode }) {
  const u = tokenStore.user();
  if (!u) return <Navigate to="/login" replace />;
  if (!allow.includes(u.role)) return <Navigate to={u.role === "Reporter" ? "/" : u.role === "Resolver" ? "/resolver" : "/admin"} replace />;
  return <>{children}</>;
}

function Home() {
  const u = tokenStore.user();
  if (!u) return <Navigate to="/login" replace />;
  if (u.role === "Reporter") return <Reporter />;
  if (u.role === "Resolver") return <Resolver />;
  return <Admin />;
}

const router = createBrowserRouter([
  { path: "/login", element: <Login /> },
  { path: "/", element: <Home /> },
  { path: "/resolver", element: <RoleGate allow={["Resolver", "Admin"]}><Resolver /></RoleGate> },
  { path: "/admin", element: <RoleGate allow={["Admin"]}><Admin /></RoleGate> },
  { path: "/workflows", element: <RoleGate allow={["Resolver", "Admin"]}><Workflows /></RoleGate> },
  { path: "/incident/:id", element: <RoleGate allow={["Reporter", "Resolver", "Admin"]}><IncidentDetail /></RoleGate> },
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <RouterProvider router={router} />
  </React.StrictMode>
);
