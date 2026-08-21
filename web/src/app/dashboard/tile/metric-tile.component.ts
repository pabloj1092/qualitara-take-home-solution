import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { TileDto } from '../../core/api/models/dashboard.model';
import { SparklineComponent } from '../sparkline/sparkline.component';
import { StatusBadgeComponent } from './status-badge.component';

function formatDelta(value: number | null, suffix: string): string | null {
  if (value === null) {
    return null;
  }
  const sign = value > 0 ? '+' : '';
  return `${sign}${value.toFixed(1)}${suffix}`;
}

@Component({
  selector: 'app-metric-tile',
  imports: [StatusBadgeComponent, SparklineComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="tile" [class.tile--rate]="tile().kind === 'rate'">
      <header class="tile__header">
        <span class="tile__label">{{ tile().label }}</span>
        <app-status-badge [status]="tile().status" [deltaPct]="tile().deltaPct" />
      </header>

      <div class="tile__value">{{ formattedValue() }}</div>

      @if (primaryDeltaText(); as delta) {
        <div class="tile__delta" [class.tile__delta--negative]="(tile().deltaPct ?? 0) < 0">
          {{ delta }}
          @if (secondaryDeltaText(); as secondary) {
            <span class="tile__delta-secondary">{{ secondary }}</span>
          }
        </div>
      }

      @if (tile().kind === 'rate' && tile().denominator !== null) {
        <div class="tile__denominator">n = {{ tile().denominator }}</div>
      }

      @if (bandText(); as band) {
        <div class="tile__band">typical {{ band }}</div>
      }

      <app-sparkline
        [series]="tile().series"
        [bandLow]="tile().bandLow"
        [bandHigh]="tile().bandHigh"
        [baselineMean]="tile().baselineMean"
        [kind]="tile().kind"
      />

      <footer class="tile__footer">{{ tile().baselineWeeksUsed }} week(s) in baseline</footer>
    </article>
  `,
  styles: `
    .tile {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      padding: 0.85rem 1rem;
      border: 1px solid #f0f0f0;
      border-radius: 8px;
      background: #ffffff;
      min-width: 220px;
    }
    .tile__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .tile__label {
      font-weight: 600;
      font-size: 0.9rem;
      color: #262626;
    }
    .tile__value {
      font-size: 1.75rem;
      font-weight: 700;
      color: #141414;
      line-height: 1.1;
    }
    .tile__delta {
      font-size: 0.85rem;
      color: #1a7f5a;
    }
    .tile__delta--negative {
      color: #c0233e;
    }
    .tile__delta-secondary {
      margin-left: 0.35em;
      color: #8c8c8c;
      font-weight: 400;
    }
    .tile__denominator,
    .tile__band,
    .tile__footer {
      font-size: 0.75rem;
      color: #8c8c8c;
    }
  `,
})
export class MetricTileComponent {
  readonly tile = input.required<TileDto>();

  readonly formattedValue = computed(() => {
    const t = this.tile();
    if (t.value === null) {
      return '—';
    }
    return t.kind === 'rate' ? `${t.value.toFixed(1)}%` : t.value.toLocaleString();
  });

  // Rate tiles display deltaPp (RequirementsFinal.md, "Percentage points, not percentages") but
  // the status ladder judges deltaPct — a printed relative percentage under a percentage value
  // reads as the new value, which is the thing that section warns against, not showing the
  // judged number at all. Surfacing it as parenthetical context is what makes a red badge
  // checkable: "+1.1pp (+5.0% vs baseline)" tells the reader the number the verdict was made on.
  readonly primaryDeltaText = computed(() => {
    const t = this.tile();
    return t.kind === 'rate' ? formatDelta(t.deltaPp, 'pp') : formatDelta(t.deltaPct, '%');
  });

  readonly secondaryDeltaText = computed(() => {
    const t = this.tile();
    if (t.kind !== 'rate') {
      return null;
    }
    const pct = formatDelta(t.deltaPct, '%');
    return pct === null ? null : `(${pct} vs baseline)`;
  });

  readonly bandText = computed(() => {
    const t = this.tile();
    if (t.bandLow === null || t.bandHigh === null) {
      return null;
    }
    const high = t.kind === 'rate' ? Math.min(100, t.bandHigh) : t.bandHigh;
    const unit = t.kind === 'rate' ? '%' : '';
    const fmt = (v: number) => (t.kind === 'rate' ? v.toFixed(1) : Math.round(v).toString());
    return `${fmt(t.bandLow)}${unit}–${fmt(high)}${unit}`;
  });
}
