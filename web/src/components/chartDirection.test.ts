import { describe, expect, it } from 'vitest';
import { chartAxes } from './chartDirection';

describe('chartAxes', () => {
  it('leaves left-to-right charts alone', () => {
    expect(chartAxes('ltr')).toEqual({});
  });

  it('passes a chart\'s own y axis props through untouched in left-to-right', () => {
    const own = { width: 170 };
    const axes = chartAxes('ltr', { yAxisProps: own });
    expect(axes).toEqual({ yAxisProps: { width: 170 } });
    expect(axes.yAxisProps).toBe(own);
    expect(axes.xAxisProps).toBeUndefined();
  });

  it('mirrors right-to-left charts: months run right to left, the value axis sits on the right', () => {
    expect(chartAxes('rtl')).toEqual({ xAxisProps: { reversed: true }, yAxisProps: { orientation: 'right' } });
  });

  it('keeps the chart\'s own y axis props when mirroring', () => {
    expect(chartAxes('rtl', { yAxisProps: { width: 170, interval: 0 } })).toEqual({
      xAxisProps: { reversed: true },
      yAxisProps: { width: 170, interval: 0, orientation: 'right' },
    });
  });

  it('keeps a custom tick when mirroring', () => {
    const tick = { fontSize: 12 };
    expect(chartAxes('rtl', { yAxisProps: { tick } }).yAxisProps?.tick).toBe(tick);
  });
});
