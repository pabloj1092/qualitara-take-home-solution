import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { TileStatus } from '../../core/api/models/dashboard.model';

interface StatusPresentation {
  icon: string;
  label: string;
}

// Deuteranopia-safe by construction: colour is never the only channel (icon + text label
// always render — WCAG 1.4.1), and the red/green pair leans toward the IBM Carbon-style
// palette (a magenta-leaning red, a blue-leaning green) that stays distinguishable rather
// than the ant design defaults, which sit close together under a deuteranopia simulation.
const PRESENTATION: Record<TileStatus, StatusPresentation> = {
  insufficientData: { icon: '●', label: 'Not enough data' },
  partialWeek: { icon: '◐', label: 'Partial week' },
  breach: { icon: '▲', label: 'Outside tolerance' },
  warning: { icon: '◆', label: 'Near tolerance' },
  normal: { icon: '✓', label: 'Normal' },
};

@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="status" [class]="'status--' + status()" role="status" [attr.aria-label]="label()">
      <span class="status__icon" aria-hidden="true">{{ icon() }}</span>
      <span class="status__label">{{ label() }}</span>
    </span>
  `,
  styles: `
    .status {
      display: inline-flex;
      align-items: center;
      gap: 0.35em;
      font-size: 0.85rem;
      font-weight: 600;
      padding: 0.1em 0.5em;
      border-radius: 999px;
    }
    .status--insufficientData,
    .status--partialWeek {
      color: #595959;
      background: #f0f0f0;
    }
    .status--breach {
      color: #ffffff;
      background: #c0233e;
    }
    .status--warning {
      color: #613400;
      background: #ffd591;
    }
    .status--normal {
      color: #ffffff;
      background: #1a7f5a;
    }
    .status__icon {
      line-height: 1;
    }
  `,
})
export class StatusBadgeComponent {
  readonly status = input.required<TileStatus>();
  /** Breach's arrow follows the direction of the deviation — positive delta points up. */
  readonly deltaPct = input<number | null>(null);

  private readonly presentation = computed(() => PRESENTATION[this.status()]);
  readonly label = computed(() => this.presentation().label);
  readonly icon = computed(() => {
    if (this.status() !== 'breach') {
      return this.presentation().icon;
    }
    const delta = this.deltaPct();
    return delta !== null && delta < 0 ? '▼' : '▲';
  });
}
