import '@mantine/core/styles.layer.css';
import '@mantine/dates/styles.layer.css';
import '@mantine/notifications/styles.layer.css';
import '@mantine/charts/styles.layer.css';
import '@mantine/dropzone/styles.layer.css';
import 'mantine-datatable/styles.layer.css';
import './styles.css';
import './i18n';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { DirectionProvider, MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { ModalsProvider } from '@mantine/modals';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router';
import { theme } from './theme';
import { AuthProvider } from './auth/AuthContext';
import { App } from './App';
import { LocaleProviders } from './LocaleProviders';
import { applyLanguage, directionOf, savedLanguage } from './language';

// The language last used on this device, until the account says otherwise (sign-in page, technician app offline).
const startLanguage = savedLanguage();
applyLanguage(startLanguage);

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: (count, err) => count < 2 && (err as { status?: number }).status !== 404 && (err as { status?: number }).status !== 403, refetchOnWindowFocus: false },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <DirectionProvider initialDirection={directionOf(startLanguage)} detectDirection={false}>
    <MantineProvider theme={theme} defaultColorScheme="light">
      <LocaleProviders>
      <QueryClientProvider client={queryClient}>
        <ModalsProvider>
          <Notifications position="top-right" />
          <BrowserRouter>
            <AuthProvider>
              <App />
            </AuthProvider>
          </BrowserRouter>
        </ModalsProvider>
      </QueryClientProvider>
      </LocaleProviders>
    </MantineProvider>
    </DirectionProvider>
  </StrictMode>,
);
