import { describe, expect, it } from 'vitest';
import { isTeamlessSport } from '../leagueSportRules';

describe('Athletics monitoring', () => {
  it.each(['Athletics', 'athletics'])('monitors %s without selecting countries', sport => {
    expect(isTeamlessSport(sport, 'Diamond League')).toBe(true);
  });

  it.each([['Soccer', 'English Premier League'], ['American Football', 'NFL'], ['Tennis', 'Davis Cup']])(
    'preserves team selection for %s', (sport, league) => {
      expect(isTeamlessSport(sport, league)).toBe(false);
    },
  );
});

describe('API league formats', () => {
  it('recognises an individual format without a sport-name exception', () => {
    expect(isTeamlessSport('New sport', 'New league', 'EventSport')).toBe(true);
  });
  it('honours a league-specific team format', () => {
    expect(isTeamlessSport('Athletics', 'Team competition', 'TeamvsTeam')).toBe(false);
  });
  it('keeps legacy rules when the format is missing', () => {
    expect(isTeamlessSport('Tennis', 'Davis Cup', null)).toBe(false);
    expect(isTeamlessSport('Athletics', 'Diamond League', null)).toBe(true);
  });
});
