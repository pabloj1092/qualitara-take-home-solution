import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { SectionDto } from '../../core/api/models/dashboard.model';
import { MetricTileComponent } from '../tile/metric-tile.component';

@Component({
  selector: 'app-event-section',
  imports: [MetricTileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="section" [attr.aria-label]="section().displayName">
      <h2 class="section__title">{{ section().displayName }}</h2>
      <div class="section__tiles">
        <app-metric-tile [tile]="section().countTile" />
        @for (rateTile of section().rateTiles; track rateTile.key) {
          <app-metric-tile [tile]="rateTile" />
        }
      </div>
    </section>
  `,
  styles: `
    .section {
      margin-bottom: 1.5rem;
    }
    .section__title {
      font-size: 1.05rem;
      font-weight: 600;
      margin: 0 0 0.6rem;
      color: #141414;
    }
    .section__tiles {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
    }
  `,
})
export class EventSectionComponent {
  readonly section = input.required<SectionDto>();
}
