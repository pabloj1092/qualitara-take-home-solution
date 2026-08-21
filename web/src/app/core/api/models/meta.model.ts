// Mirrors src/Relay.Api/Dtos/MetaResponseDto.cs field-for-field.

export interface MetaLocationDto {
  id: number;
  name: string;
  openedOn: string | null;
  closedOn: string | null;
}

export interface MetaDefaultsDto {
  week: string | null;
  window: number;
  tolerancePct: number;
  minBaselineEvents: number;
  minRateDenominator: number;
  minHistoryWeeks: number;
  minWeekCompleteness: number;
  amberFraction: number;
}

export interface MetaResponseDto {
  locations: MetaLocationDto[];
  firstWeek: string | null;
  latestWeekWithData: string | null;
  latestCompleteWeek: string | null;
  maxWindowForWeek: number;
  defaults: MetaDefaultsDto;
}
