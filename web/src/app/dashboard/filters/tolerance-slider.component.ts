import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzSliderModule } from 'ng-zorro-antd/slider';

import { DashboardStore } from '../dashboard-store';

const DEFAULT_TOLERANCE = 40;

@Component({
  selector: 'app-tolerance-slider',
  imports: [FormsModule, NzSliderModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="filter">
      <span class="filter__label">Tolerance: {{ value() }}%</span>
      <nz-slider
        [ngModel]="value()"
        (ngModelChange)="onChange($event)"
        [nzMin]="1"
        [nzMax]="100"
        [nzStep]="1"
        [attr.aria-label]="'Tolerance percentage'"
      />
    </label>
  `,
  styles: `
    .filter {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 180px;
    }
    .filter__label {
      font-size: 0.75rem;
      font-weight: 600;
      color: #595959;
    }
  `,
})
export class ToleranceSliderComponent {
  private readonly store = inject(DashboardStore);

  // Bound to the raw filter, not the resolved response — the store debounces the HTTP call,
  // but every drag tick must still move the handle and update the URL immediately (rapid
  // %tolerance changes cancel in-flight requests; the *slider itself* is never debounced).
  readonly value = computed(() => this.store.filters().tolerance ?? this.store.response()?.tolerancePct ?? DEFAULT_TOLERANCE);

  onChange(value: number): void {
    this.store.setTolerance(value);
  }
}
