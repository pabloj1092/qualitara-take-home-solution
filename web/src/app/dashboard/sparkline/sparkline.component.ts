import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { SeriesPointDto, TileKind } from '../../core/api/models/dashboard.model';

const WIDTH = 240;
const HEIGHT = 56;
const PAD_Y = 6;
const PAD_X = 4;

interface PlottedPoint {
  x: number;
  y: number | null;
  point: SeriesPointDto;
  title: string;
}

let nextInstanceId = 0;

/**
 * Hand-rolled inline SVG — ng-zorro ships no charts, and ~9 points with a shaded band is not
 * worth a chart library (RequirementsFinal.md, Frontend). Deliberately not pixel-tested: the
 * series data is what's asserted, not the markup (Test plan, "Deliberately not tested").
 */
@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      [attr.viewBox]="'0 0 ' + width + ' ' + height"
      role="img"
      [attr.aria-label]="summary()"
      xmlns="http://www.w3.org/2000/svg"
    >
      <defs>
        <pattern
          [attr.id]="hatchId"
          patternUnits="userSpaceOnUse"
          width="6"
          height="6"
          patternTransform="rotate(45)"
        >
          <line x1="0" y1="0" x2="0" y2="6" stroke="currentColor" stroke-width="2" />
        </pattern>
      </defs>

      @if (bandRect(); as band) {
        <rect
          [attr.x]="padX"
          [attr.y]="band.y"
          [attr.width]="width - 2 * padX"
          [attr.height]="band.height"
          class="sparkline__band"
        />
        <rect
          [attr.x]="padX"
          [attr.y]="band.y"
          [attr.width]="width - 2 * padX"
          [attr.height]="band.height"
          [attr.fill]="'url(#' + hatchId + ')'"
          class="sparkline__band-hatch"
        />
      }

      @for (segment of exclusionSegments(); track segment.x) {
        <rect
          [attr.x]="segment.x"
          y="0"
          [attr.width]="segment.width"
          [attr.height]="height"
          [attr.fill]="'url(#' + hatchId + ')'"
          class="sparkline__exclusion"
        />
      }

      @if (baselineY(); as y) {
        <line
          [attr.x1]="padX"
          [attr.x2]="width - padX"
          [attr.y1]="y"
          [attr.y2]="y"
          class="sparkline__baseline"
          stroke-dasharray="2 2"
        />
      }

      @for (segment of valueSegments(); track $index) {
        <polyline [attr.points]="segment" class="sparkline__line" />
      }

      @for (p of points(); track p.point.weekStart) {
        @if (p.y !== null) {
          <circle
            [attr.cx]="p.x"
            [attr.cy]="p.y"
            [attr.r]="p.point.isViewedWeek ? 4 : 2.5"
            class="sparkline__point"
            [class.sparkline__point--dimmed]="isLowDenominator(p.point)"
            [class.sparkline__point--excluded]="!p.point.includedInBaseline"
            [class.sparkline__point--viewed]="p.point.isViewedWeek"
          >
            <title>{{ p.title }}</title>
          </circle>
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: block;
      color: #8c8c8c;
    }
    svg {
      width: 100%;
      height: auto;
      overflow: visible;
    }
    .sparkline__band {
      fill: #1677ff;
      opacity: 0.12;
    }
    .sparkline__band-hatch {
      opacity: 0.35;
      color: #1677ff;
    }
    .sparkline__exclusion {
      opacity: 0.5;
      color: #d46b08;
    }
    .sparkline__baseline {
      stroke: #595959;
      stroke-width: 1;
    }
    .sparkline__line {
      fill: none;
      stroke: #1677ff;
      stroke-width: 1.5;
    }
    .sparkline__point {
      fill: #1677ff;
      stroke: none;
    }
    .sparkline__point--dimmed {
      opacity: 0.4;
    }
    .sparkline__point--excluded {
      fill: #ffffff;
      stroke: #1677ff;
      stroke-width: 1.5;
      stroke-dasharray: 1.5 1;
    }
    .sparkline__point--viewed {
      stroke: #262626;
      stroke-width: 1.5;
    }
  `,
})
export class SparklineComponent {
  readonly series = input.required<SeriesPointDto[]>();
  readonly bandLow = input<number | null>(null);
  readonly bandHigh = input<number | null>(null);
  readonly baselineMean = input<number | null>(null);
  readonly kind = input.required<TileKind>();
  /** Below this denominator a rate point is dimmed — mirrors min_rate_denominator visually. */
  readonly minRateDenominator = input(20);

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly padX = PAD_X;
  protected readonly hatchId = `sparkline-hatch-${nextInstanceId++}`;

  /** Rate bands clamp at 100% on display — never mutate the payload, only the rendering. */
  private readonly displayBandHigh = computed(() => {
    const high = this.bandHigh();
    if (high === null) {
      return null;
    }
    return this.kind() === 'rate' ? Math.min(100, high) : high;
  });

  private readonly domain = computed<[number, number]>(() => {
    const values = this.series()
      .map((p) => p.value)
      .filter((v): v is number => v !== null);
    const candidates = [...values, this.bandLow(), this.displayBandHigh(), this.baselineMean()].filter(
      (v): v is number => v !== null,
    );

    if (candidates.length === 0) {
      return [0, 1];
    }

    let min = Math.min(...candidates);
    let max = Math.max(...candidates);
    if (min === max) {
      min -= 1;
      max += 1;
    }
    return [min, max];
  });

  private readonly step = computed(() => {
    const n = this.series().length;
    return n > 1 ? (this.width - 2 * this.padX) / (n - 1) : 0;
  });

  private yFor(value: number): number {
    const [min, max] = this.domain();
    const ratio = (value - min) / (max - min);
    return this.height - PAD_Y - ratio * (this.height - 2 * PAD_Y);
  }

  private xFor(index: number): number {
    return this.padX + index * this.step();
  }

  readonly points = computed<PlottedPoint[]>(() =>
    this.series().map((point, index) => ({
      x: this.xFor(index),
      y: point.value === null ? null : this.yFor(point.value),
      point,
      title: this.pointTitle(point),
    })),
  );

  private pointTitle(point: SeriesPointDto): string {
    const base = `${point.daysIncluded} of ${point.expectedDays} days`;
    return point.includedInBaseline ? base : `${base} · excluded from baseline`;
  }

  isLowDenominator(point: SeriesPointDto): boolean {
    return point.denominator !== null && point.denominator < this.minRateDenominator();
  }

  readonly baselineY = computed(() => {
    const mean = this.baselineMean();
    return mean === null ? null : this.yFor(mean);
  });

  readonly bandRect = computed<{ y: number; height: number } | null>(() => {
    const low = this.bandLow();
    const high = this.displayBandHigh();
    if (low === null || high === null) {
      return null;
    }
    const yTop = this.yFor(high);
    const yBottom = this.yFor(low);
    return { y: yTop, height: Math.max(yBottom - yTop, 1) };
  });

  /** `value === null` breaks the line rather than interpolating across it. */
  readonly valueSegments = computed<string[]>(() => {
    const segments: string[] = [];
    let current: string[] = [];
    for (const p of this.points()) {
      if (p.y === null) {
        if (current.length > 1) {
          segments.push(current.join(' '));
        }
        current = [];
        continue;
      }
      current.push(`${p.x},${p.y}`);
    }
    if (current.length > 1) {
      segments.push(current.join(' '));
    }
    return segments;
  });

  readonly exclusionSegments = computed<{ x: number; width: number }[]>(() => {
    const halfStep = this.step() / 2;
    return this.points()
      .filter((p) => p.point.overlapsExclusion)
      .map((p) => ({ x: Math.max(0, p.x - halfStep), width: this.step() || 6 }));
  });

  readonly summary = computed(() => {
    const values = this.series().map((p) => (p.value === null ? 'no data' : p.value));
    const unit = this.kind() === 'rate' ? '%' : '';
    return `Trend: ${values.map((v) => (typeof v === 'number' ? `${v}${unit}` : v)).join(', ')}`;
  });
}
