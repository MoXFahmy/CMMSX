import { Center, Loader } from '@mantine/core';
import { Navigate, Route, Routes, useLocation, useSearchParams } from 'react-router';
import { useAuth } from './auth/AuthContext';
import { AppLayout } from './components/AppLayout';
import { LoginPage, RegisterPage } from './pages/AuthPages';
import { DashboardPage } from './pages/DashboardPage';
import { LocationsPage } from './pages/locations/LocationsPage';
import { AssetsPage } from './pages/assets/AssetsPage';
import { AssetDetailPage } from './pages/assets/AssetDetailPage';
import { AssetFormPage } from './pages/assets/AssetFormPage';
import { SettingsPage } from './pages/settings/SettingsPage';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { WorkOrdersPage } from './pages/work/WorkOrdersPage';
import { WorkOrderDetailPage } from './pages/work/WorkOrderDetailPage';
import { WorkOrderFormPage } from './pages/work/WorkOrderFormPage';
import { WorkRequestsPage } from './pages/work/WorkRequestsPage';
import { PeoplePage } from './pages/people/PeoplePage';
import { PmPage } from './pages/pm/PmPage';
import { PmScheduleFormPage } from './pages/pm/PmScheduleFormPage';
import { InventoryPage } from './pages/inventory/InventoryPage';
import { PartDetailPage } from './pages/inventory/PartDetailPage';
import { PartFormPage } from './pages/inventory/PartFormPage';
import { PurchasingPage } from './pages/purchasing/PurchasingPage';
import { PoFormPage } from './pages/purchasing/PoFormPage';
import { PoDetailPage, PoPrintPage } from './pages/purchasing/PoDetailPage';
import { ReportsPage, ReportViewPage } from './pages/reports/ReportsPage';

export function App() {
  const { me, loading } = useAuth();
  const location = useLocation();
  if (loading) return <Center h="100vh"><Loader /></Center>;
  if (!me) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="*" element={<Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />} />
      </Routes>
    );
  }
  return (
    <Routes>
      <Route path="purchasing/orders/:id/print" element={<PoPrintPage />} />
      <Route element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="locations" element={<LocationsPage />} />
        <Route path="assets" element={<AssetsPage />} />
        <Route path="assets/new" element={<AssetFormPage />} />
        <Route path="assets/:id" element={<AssetDetailPage />} />
        <Route path="assets/:id/edit" element={<AssetFormPage />} />
        <Route path="work-orders" element={<WorkOrdersPage />} />
        <Route path="work-orders/new" element={<WorkOrderFormPage />} />
        <Route path="work-orders/:id" element={<WorkOrderDetailPage />} />
        <Route path="work-orders/:id/edit" element={<WorkOrderFormPage />} />
        <Route path="work-requests" element={<WorkRequestsPage />} />
        <Route path="people" element={<PeoplePage />} />
        <Route path="pm" element={<PmPage />} />
        <Route path="pm/schedules/new" element={<PmScheduleFormPage />} />
        <Route path="pm/schedules/:id" element={<PmScheduleFormPage />} />
        <Route path="pm/:tab" element={<PmPage />} />
        <Route path="inventory" element={<InventoryPage />} />
        <Route path="inventory/parts/new" element={<PartFormPage />} />
        <Route path="inventory/parts/:id" element={<PartDetailPage />} />
        <Route path="inventory/parts/:id/edit" element={<PartFormPage />} />
        <Route path="inventory/:tab" element={<InventoryPage />} />
        <Route path="purchasing" element={<PurchasingPage />} />
        <Route path="purchasing/orders/new" element={<PoFormPage />} />
        <Route path="purchasing/orders/:id" element={<PoDetailPage />} />
        <Route path="purchasing/orders/:id/edit" element={<PoFormPage />} />
        <Route path="purchasing/:tab" element={<PurchasingPage />} />
        <Route path="reports" element={<ReportsPage />} />
        <Route path="reports/:key" element={<ReportViewPage />} />
        <Route path="settings/*" element={<SettingsPage />} />
        <Route path="soon/:module" element={<ComingSoonPage />} />
        <Route path="login" element={<AfterLogin />} />
        <Route path="register" element={<AfterLogin />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

/** Once signed in, leave /login for the page the user originally asked for (same-origin paths only). */
function AfterLogin() {
  const [params] = useSearchParams();
  const next = params.get('next');
  const safe = next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/login') ? next : '/';
  return <Navigate to={safe} replace />;
}
