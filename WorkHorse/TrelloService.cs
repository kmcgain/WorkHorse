using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrelloNet;

namespace WorkHorse
{
    public class TrelloService
    {
        public delegate string TrelloAuthorisationRequest(Uri requestUri);

        private readonly Trello trello;

        private Board dailyBoard;

        private List currentList;

        private List doneList;

        private bool alreadySetup = false;

        public TrelloService()
        {
            trello = new Trello("b2260b9a6ad59a9e7f2cabd2716578a6");
        }

        private void setupBoards()
        {
            if (alreadySetup) return;

            dailyBoard = this.trello.Boards.ForMe().SingleOrDefault(_ => _.Name == "DailyWork") ??
                         trello.Boards.Add("DailyWork");                

            var dailyLists = trello.Lists.ForBoard(dailyBoard).ToList();

            currentList = currentList = dailyLists.SingleOrDefault(_ => _.Name == "Current") ??
                                        trello.Lists.Add("Current", dailyBoard);

            doneList = doneList = dailyLists.SingleOrDefault(_ => _.Name == "Done") ??
                                  trello.Lists.Add("Done", dailyBoard);

            alreadySetup = true;
        }

        public void MoveCardToDone(Card card)
        {
            setupBoards();

            trello.Async.Cards.Move(card, doneList);                    
        }

        public void ChangeName(Card card, string newName)
        {
            setupBoards();

            trello.Async.Cards.ChangeName(card, newName);
            card.Name = newName;
        }

        public void ChangeDescription(Card card, string description)
        {
            setupBoards();

            trello.Async.Cards.ChangeDescription(card, description);
            card.Desc = description;
        }

        public void ChangeCardPos(Card card, double newPos)
        {
            setupBoards();

            trello.Async.Cards.ChangePos(card, newPos);
            card.Pos = newPos;
        }

        public Task<IEnumerable<Card>> CurrentCards()
        {
            setupBoards();
            
            return trello.Async.Cards.ForList(currentList);
        }

        public Card CreateCard(string cardName)
        {
            setupBoards();

            return trello.Cards.Add(cardName, currentList);
        }

        public string Authorise(TrelloAuthorisationRequest getAuthFromUser)
        {            
            while (true)
            {
                try
                {
                    var auth = authoriseTrello(getAuthFromUser);
                    trello.Boards.ForMe();
                    return auth;
                }
                catch (TrelloException e)
                {
                    
                }
            }
        }

        private string authoriseTrello(TrelloAuthorisationRequest getAuthFromUser)
        {                            
            var url = trello.GetAuthorizationUrl("Work Horse", Scope.ReadWrite);
            var authFromUser = getAuthFromUser(url);

            trello.Authorize(authFromUser);
            return authFromUser;
        }
    }
}