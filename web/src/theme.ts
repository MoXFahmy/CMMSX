import { createTheme, type MantineColorsTuple } from '@mantine/core';

const brand: MantineColorsTuple = ['#e7f5ff', '#d0ebff', '#a5d8ff', '#74c0fc', '#4dabf7', '#339af0', '#228be6', '#1c7ed6', '#1971c2', '#1864ab'];

export const theme = createTheme({
  primaryColor: 'brand',
  colors: { brand },
  defaultRadius: 'md',
  fontFamily: 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Arabic", sans-serif',
  headings: { fontWeight: '650' },
  components: {
    Card: { defaultProps: { withBorder: true, radius: 'md', padding: 'lg' } },
    Paper: { defaultProps: { radius: 'md' } },
    Badge: { defaultProps: { variant: 'light', radius: 'sm' } },
    Modal: { defaultProps: { centered: true, overlayProps: { blur: 2 } } },
  },
});
