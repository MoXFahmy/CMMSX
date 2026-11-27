import type { XAxisProps, YAxisProps } from 'recharts';
import { useDirection } from '@mantine/core';

export type ChartAxes = { xAxisProps?: XAxisProps; yAxisProps?: YAxisProps };

/**
 * Axis props that make a chart follow the reading direction. Left to right: nothing changes (the chart's own
 * `yAxisProps` pass through untouched). Right to left: the x axis runs from the right and the y axis sits on the right.
 * A chart's own `yAxisProps` (width, interval, a custom tick) are kept, never replaced.
 */
export function chartAxes(dir: 'ltr' | 'rtl', opts: { yAxisProps?: YAxisProps } = {}): ChartAxes {
  const { yAxisProps } = opts;
  if (dir !== 'rtl') return yAxisProps ? { yAxisProps } : {};
  return { xAxisProps: { reversed: true }, yAxisProps: { ...yAxisProps, orientation: 'right' } };
}

/** `chartAxes` for the current page direction; spread the result onto a Mantine BarChart / LineChart. */
export function useChartAxes(opts?: { yAxisProps?: YAxisProps }): ChartAxes {
  return chartAxes(useDirection().dir, opts);
}
