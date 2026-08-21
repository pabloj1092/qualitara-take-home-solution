import { ChangeDetectionStrategy, Component } from '@angular/core';

import { LocationSelectComponent } from './location-select.component';
import { ToleranceSliderComponent } from './tolerance-slider.component';
import { WeekPickerComponent } from './week-picker.component';
import { WindowSelectComponent } from './window-select.component';

@Component({
  selector: 'app-filter-bar',
  imports: [LocationSelectComponent, WindowSelectComponent, ToleranceSliderComponent, WeekPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filter-bar">
      <app-location-select />
      <app-window-select />
      <app-tolerance-slider />
      <app-week-picker />
    </div>
  `,
  styles: `
    .filter-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 1.5rem;
      padding: 1rem;
      background: #fafafa;
      border: 1px solid #f0f0f0;
      border-radius: 8px;
      margin-bottom: 1.25rem;
    }
  `,
})
export class FilterBarComponent {}
