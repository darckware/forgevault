import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "@/components/RequireAuth";
import { AppLayout } from "@/components/layout/AppLayout";
import { LoginPage } from "@/pages/LoginPage";
import { OrganizationsPage } from "@/pages/OrganizationsPage";
import { OrganizationDetailPage } from "@/pages/OrganizationDetailPage";
import { ProjectDetailPage } from "@/pages/ProjectDetailPage";
import { EnvironmentDetailPage } from "@/pages/EnvironmentDetailPage";
import { SecretDetailPage } from "@/pages/SecretDetailPage";
import { ServiceAccountsPage } from "@/pages/ServiceAccountsPage";
import { ServiceAccountDetailPage } from "@/pages/ServiceAccountDetailPage";
import { AccessRolesPage } from "@/pages/AccessRolesPage";
import { AuditPage } from "@/pages/AuditPage";
import { NotFoundPage } from "@/pages/NotFoundPage";

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireAuth>
            <AppLayout />
          </RequireAuth>
        }
      >
        <Route path="/" element={<Navigate to="/organizations" replace />} />
        <Route path="/organizations" element={<OrganizationsPage />} />
        <Route path="/organizations/:orgId" element={<OrganizationDetailPage />} />
        <Route path="/projects/:projectId" element={<ProjectDetailPage />} />
        <Route path="/environments/:envId" element={<EnvironmentDetailPage />} />
        <Route path="/secrets/:secretId" element={<SecretDetailPage />} />
        <Route path="/service-accounts" element={<ServiceAccountsPage />} />
        <Route path="/service-accounts/:saId" element={<ServiceAccountDetailPage />} />
        <Route path="/access" element={<AccessRolesPage />} />
        <Route path="/audit" element={<AuditPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
