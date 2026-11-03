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
      <Route element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="locations" element={<LocationsPage />} />
        <Route path="assets" element={<AssetsPage />} />
        <Route path="assets/new" element={<AssetFormPage />} />
        <Route path="assets/:id" element={<AssetDetailPage />} />
        <Route path="assets/:id/edit" element={<AssetFormPage />} />
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
