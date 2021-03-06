﻿using System;
using System.Linq;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using LoLTournament.Helpers;

namespace LoLTournament.Models
{
    public class StatisticsViewModel
    {

        public double AvgKills { get; set; }
        public double AvgDeaths { get; set; }
        public double AvgAssists { get; set; }
        public double TotalGames { get; set; }
        public double BlueSideWinPercentage { get; set; }
        public TimeSpan AvgMatchDuration { get; set; }

        public StatisticsViewModel()
        {
            TotalGames = Mongo.Matches.Count(Query<Match>.Where(x => x.Finished));
            var matches = Mongo.Matches.FindAll();

            if(matches.Count() > 0)
            {
                AvgKills = matches.Sum(x => x.KillsBlueTeam + x.KillsPurpleTeam) / TotalGames;
                AvgDeaths = matches.Sum(x => x.DeathsBlueTeam + x.DeathsPurpleTeam) / TotalGames;
                AvgAssists = matches.Sum(x => x.AssistsBlueTeam + x.AssistsPurpleTeam) / TotalGames;
                AvgMatchDuration = TimeSpan.FromSeconds(matches.Average(x => x.Duration.TotalSeconds));
                BlueSideWinPercentage = matches.Where(match => match.Finished).Where(match => match.BlueTeamId == match.WinnerId).Count() / TotalGames;
            }
            else
            {
                AvgKills = 0;
                AvgDeaths = 0;
                AvgAssists = 0;
                BlueSideWinPercentage = 0;
                AvgMatchDuration = TimeSpan.Zero;
            }
            
        }
    }
}
