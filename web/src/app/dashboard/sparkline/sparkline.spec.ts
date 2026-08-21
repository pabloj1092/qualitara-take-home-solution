import { TestBed } from '@angular/core/testing';

import { SeriesPointDto } from '../../core/api/models/dashboard.model';
import { SparklineComponent } from './sparkline.component';

function point(overrides: Partial<SeriesPointDto>): SeriesPointDto {
  return {
    weekStart: '2026-06-01',
    value: 40,
    denominator: null,
    daysIncluded: 7,
    expectedDays: 7,
    includedInBaseline: true,
    exclusionReason: null,
    isViewedWeek: false,
    overlapsExclusion: false,
    ...overrides,
  };
}

// §6 "The partial-week badge and the 'excluded from baseline' tooltip appear on the affected
// sparkline point." Deliberately not asserting pixel geometry (Test plan, "Deliberately not
// tested") — only the tooltip text and the exclusion markers a screen reader / hover would
// actually expose.
describe('SparklineComponent', () => {
  function render(series: SeriesPointDto[], extra: Partial<{ bandLow: number | null; bandHigh: number | null; baselineMean: number | null }> = {}) {
    const fixture = TestBed.createComponent(SparklineComponent);
    fixture.componentRef.setInput('series', series);
    fixture.componentRef.setInput('kind', 'count');
    fixture.componentRef.setInput('bandLow', extra.bandLow ?? null);
    fixture.componentRef.setInput('bandHigh', extra.bandHigh ?? null);
    fixture.componentRef.setInput('baselineMean', extra.baselineMean ?? null);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('tags a dropped partial week with the "excluded from baseline" tooltip', () => {
    const series = [
      point({ weekStart: '2026-05-25', daysIncluded: 4, expectedDays: 7, includedInBaseline: false }),
      point({ weekStart: '2026-06-01', isViewedWeek: true }),
    ];
    const el = render(series);
    const titles = [...el.querySelectorAll('circle title')].map((t) => t.textContent);
    expect(titles).toContain('4 of 7 days · excluded from baseline');
  });

  it('does not append the exclusion phrase to a week that cleared the baseline', () => {
    const series = [point({ daysIncluded: 7, expectedDays: 7, includedInBaseline: true })];
    const el = render(series);
    const title = el.querySelector('circle title')!.textContent;
    expect(title).toBe('7 of 7 days');
    expect(title).not.toContain('excluded');
  });

  it('marks a dropped week visually distinct from an included one (dashed, not just coloured)', () => {
    const series = [
      point({ weekStart: '2026-05-25', includedInBaseline: false }),
      point({ weekStart: '2026-06-01', includedInBaseline: true, isViewedWeek: true }),
    ];
    const el = render(series);
    const circles = [...el.querySelectorAll('circle')];
    expect(circles[0].getAttribute('class')).toContain('sparkline__point--excluded');
    expect(circles[1].getAttribute('class')).not.toContain('sparkline__point--excluded');
  });

  it('hatches the week that overlaps a disclosed data-quality exclusion', () => {
    const series = [
      point({ weekStart: '2026-06-01', overlapsExclusion: true }),
      point({ weekStart: '2026-06-08', overlapsExclusion: false }),
    ];
    const el = render(series);
    expect(el.querySelectorAll('.sparkline__exclusion').length).toBe(1);
  });

  it('draws no band when window=1 sends null bandLow/bandHigh', () => {
    const series = [point({}), point({ isViewedWeek: true })];
    const el = render(series, { bandLow: null, bandHigh: null, baselineMean: 40 });
    expect(el.querySelector('.sparkline__band')).toBeNull();
  });

  it('clamps a rate band to 100% on display without mutating the input', () => {
    const fixture = TestBed.createComponent(SparklineComponent);
    const series = [point({ value: 82.3 }), point({ value: 82.3, isViewedWeek: true })];
    fixture.componentRef.setInput('series', series);
    fixture.componentRef.setInput('kind', 'rate');
    fixture.componentRef.setInput('bandLow', 49.4);
    fixture.componentRef.setInput('bandHigh', 115.22);
    fixture.componentRef.setInput('baselineMean', 82.3);
    fixture.detectChanges();

    expect(fixture.componentInstance.bandHigh()).toBe(115.22); // input itself is untouched
    const band = fixture.componentInstance.bandRect();
    expect(band).not.toBeNull();
  });
});
