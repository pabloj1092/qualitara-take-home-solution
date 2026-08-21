// Mirrors src/Relay.Api/Dtos/DashboardResponseDto.cs field-for-field. Enums are serialized
// camelCase by Program.cs's JsonStringEnumConverter(JsonNamingPolicy.CamelCase).

export type TileKind = 'count' | 'rate';

export type TileStatus = 'insufficientData' | 'partialWeek' | 'breach' | 'warning' | 'normal';

export type ReasonCode =
  | 'baselineBelowMinEvents'
  | 'baselineZero'
  | 'insufficientHistory'
  | 'denominatorBelowMin'
  | 'viewedWeekPartial'
  | 'outsideTolerance'
  | 'nearTolerance'
  | 'withinTolerance'
  | 'goodDirection'
  | 'neutralPolarity';

export type SeriesExclusionReason =
  | 'partialWeek'
  | 'dataQualityExclusion'
  | 'belowMinDenominator'
  | 'noDenominator';

export interface WeekDto {
  isoWeek: string;
  start: string;
  end: string;
  label: string;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface WindowDto {
  requested: number;
  effective: number;
}

export interface LocationDto {
  id: number;
  name: string;
  selected: boolean;
}

export interface SeriesPointDto {
  weekStart: string;
  value: number | null;
  denominator: number | null;
  daysIncluded: number;
  expectedDays: number;
  includedInBaseline: boolean;
  exclusionReason: SeriesExclusionReason | null;
  isViewedWeek: boolean;
  overlapsExclusion: boolean;
}

export interface TileDto {
  key: string;
  label: string;
  kind: TileKind;
  value: number | null;
  deltaPct: number | null;
  deltaPp: number | null;
  baselineMean: number | null;
  bandLow: number | null;
  bandHigh: number | null;
  denominator: number | null;
  status: TileStatus;
  reasonCode: ReasonCode;
  baselineWeeksUsed: number;
  series: SeriesPointDto[];
}

export interface SectionDto {
  eventType: string;
  displayName: string;
  sortOrder: number;
  countTile: TileDto;
  rateTiles: TileDto[];
}

export interface ExclusionDto {
  fromDate: string;
  toDate: string;
  reason: string;
  weeksAffected: string[];
}

export interface DisclosuresDto {
  nullOutcomeCount: number;
  exclusions: ExclusionDto[];
}

export interface DashboardResponseDto {
  accountId: number;
  accountName: string;
  timezone: string;
  timezoneNote: string;
  week: WeekDto;
  window: WindowDto;
  tolerancePct: number;
  locations: LocationDto[];
  sections: SectionDto[];
  disclosures: DisclosuresDto;
}

export interface ApiError {
  status: number;
  title: string;
  detail: string;
  parameter?: string;
}
