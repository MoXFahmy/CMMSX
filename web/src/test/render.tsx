import type { ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router';

/** A component as the server would render it (no DOM in these tests): enough to check what it shows. Texts are in English (the app's translations load with the components). */
export const renderHtml = (node: ReactNode) => renderToStaticMarkup(
  <MantineProvider><QueryClientProvider client={new QueryClient()}><MemoryRouter>{node}</MemoryRouter></QueryClientProvider></MantineProvider>,
);

/** The text of the button that is shown pressed (aria-pressed="true"), or null. */
export const pressed = (html: string) => {
  const m = html.match(/<button[^>]*aria-pressed="true"[^>]*>([\s\S]*?)<\/button>/);
  return m ? m[1].replace(/<[^>]+>/g, '') : null;
};
