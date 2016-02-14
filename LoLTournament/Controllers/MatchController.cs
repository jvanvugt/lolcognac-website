﻿using System;
using System.Linq;
using System.Net;
using System.Web.Configuration;
using System.Web.Mvc;
using LoLTournament.Helpers;
using LoLTournament.Models;
using LoLTournament.Models.TournamentApi;
using MongoDB.Driver.Builders;
using RiotSharp;

namespace LoLTournament.Controllers
{
    public class MatchController : Controller
    {
        private readonly TournamentRiotApi _tournamentApi;

        public MatchController()
        {
            var tournamentKey = WebConfigurationManager.AppSettings["RiotTournamentApiKey"];
            var rateLimit1 = int.Parse(WebConfigurationManager.AppSettings["RateLimitPer10Seconds"]);
            var rateLimit2 = int.Parse(WebConfigurationManager.AppSettings["RateLimitPer10Minutes"]);

            _tournamentApi = TournamentRiotApi.GetInstance(tournamentKey, rateLimit1, rateLimit2);
        }

        // POST: Match/Callback
        [HttpPost]
        public ActionResult Callback(CallbackResult obj)
        {
            if (obj == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // Get the match from the database by looking up the tournament code
            var match = Mongo.Matches.FindOne(Query<Match>.Where(x => x.TournamentCode == obj.TournamentCode || x.TournamentCodeBlind == obj.TournamentCode));

            if (match == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // Check which side won
            var winningTeam = obj.WinningTeam.Select(y => y.SummonerId);
            var losingTeam = obj.LosingTeam.Select(y => y.SummonerId);

            var blueSideWon = match.BlueTeam.Participants.All(x => winningTeam.Contains(x.Summoner.Id));
            var redSideWon = match.RedTeam.Participants.All(x => winningTeam.Contains(x.Summoner.Id));

            var blueSideLost = match.BlueTeam.Participants.All(x => losingTeam.Contains(x.Summoner.Id));
            var redSideLost = match.RedTeam.Participants.All(x => losingTeam.Contains(x.Summoner.Id));

            // Check if the results are sane
            if (blueSideWon && redSideLost || redSideWon && blueSideLost)
            {
                // Set winner ID to blue if blue side won.
                match.WinnerId = blueSideWon ? match.BlueTeamId : match.RedTeamId;

                // Get more match details
                var matchDetails = _tournamentApi.GetTournamentMatch(Region.euw, obj.GameId, match.TournamentCode, false);

                // Get the players that were supposed to be on blue side
                var supposedToBeBluePlayers = matchDetails.Participants.Where(
                        x => match.BlueTeam.Participants.Select(y => y.Summoner.Id).Contains(x.ParticipantId)); // ParticipantIdentities, no summoner id's, only names
                // Set the team id to the side they actually played
                var blueTeamId = supposedToBeBluePlayers.First().TeamId;

                // Get the players that were supposed to be on red side
                var supposedToBeRedPlayers = matchDetails.Participants.Where(
                        x => match.RedTeam.Participants.Select(y => y.Summoner.Id).Contains(x.ParticipantId));
                // Set the team id to the side they actually played
                var redTeamId = supposedToBeRedPlayers.First().TeamId;

                // Set statistics
                match.Duration = matchDetails.MatchDuration;
                match.CreationTime = matchDetails.MatchCreation;
                match.StartTime = obj.StartTime;
                match.FinishDate = DateTime.UtcNow;
                match.RiotMatchId = matchDetails.MatchId;
                match.Finished = true;
                
                match.ChampionIds = matchDetails.Participants.Select(x => x.ChampionId).ToArray();

                // Exclude null bans for blind pick and for teams that forgot all their bans
                match.BanIds = matchDetails.Teams.Where(x => x.Bans != null).SelectMany(x => x.Bans).Select(x => x.ChampionId).ToArray();

                match.AssistsBlueTeam = matchDetails.Participants.Where(x => x.TeamId == blueTeamId).Sum(x => x.Stats.Assists);
                match.KillsBlueTeam = matchDetails.Participants.Where(x => x.TeamId == blueTeamId).Sum(x => x.Stats.Kills);
                match.DeathsBlueTeam = matchDetails.Participants.Where(x => x.TeamId == blueTeamId).Sum(x => x.Stats.Deaths);

                match.AssistsRedTeam = matchDetails.Participants.Where(x => x.TeamId == redTeamId).Sum(x => x.Stats.Assists);
                match.KillsRedTeam = matchDetails.Participants.Where(x => x.TeamId == redTeamId).Sum(x => x.Stats.Kills);
                match.DeathsRedTeam = matchDetails.Participants.Where(x => x.TeamId == redTeamId).Sum(x => x.Stats.Deaths);

                if (blueTeamId == 200)
                    match.PlayedWrongSide = true;

                // Save to database
                Mongo.Matches.Save(match);

                // Call the new match hook
                RiotApiScrapeJob.NewMatch(match);
            }
            else
            {
                // Teams did not have correct summoners playing the match. Match invalid.
                match.Invalid = true;
                match.InvalidReason = "INCORRECT_SUMMONERS";

                // Save to database
                Mongo.Matches.Save(match);
            }

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }
    }
}
