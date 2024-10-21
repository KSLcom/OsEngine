﻿using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Market.Servers;
using OsEngine.Market;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System.Threading;

namespace OsEngine.Robots.BotsFromStartLessons
{
    [Bot("Lesson8Bot1")]
    public class Lesson8Bot1 : BotPanel
    {
        BotTabSimple _tabToTrade;

        StrategyParameterString _regime;
        StrategyParameterString _volumeType;
        StrategyParameterDecimal _volume;
        StrategyParameterString _tradeAssetInPortfolio;

        StrategyParameterInt _millisecondsToSleepWorker;
        StrategyParameterInt _countBidsToCheck;
        StrategyParameterDecimal _percentInFirstBid;
        StrategyParameterInt _slippagePriceStep;

        StrategyParameterInt _stopPriceStep;
        StrategyParameterInt _profitPriceStep;

        public Lesson8Bot1(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tabToTrade = TabsSimple[0];

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On" });

            _volumeType = CreateParameter("Volume type", "Deposit percent", new[] { "Contracts", "Contract currency", "Deposit percent" });
            _volume = CreateParameter("Volume", 20, 1.0m, 50, 4);
            _tradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime");

            _millisecondsToSleepWorker = CreateParameter("Worker seconds to sleep", 1000, 1, 50, 4);
            _countBidsToCheck = CreateParameter("Count bids to check", 3, 1, 50, 4);
            _percentInFirstBid = CreateParameter("Percent in first bid", 100, 1.0m, 50, 4);
            _slippagePriceStep = CreateParameter("Slippage price step", -1, 1, 50, 4);
            _stopPriceStep = CreateParameter("Stop price step", 10, 1, 50, 4);
            _profitPriceStep = CreateParameter("Profit price step", 5, 1, 50, 4);

            Thread worker = new Thread(WorkerPlace);
            worker.Start();
        }

        private void WorkerPlace()
        {
            while(true) // цикл while. Бесконечный
            {
                try
                {
                    Thread.Sleep(_millisecondsToSleepWorker.ValueInt);

                    if (_regime.ValueString == "Off")
                    {
                        continue;
                    }

                    if (_tabToTrade.IsConnected == false
                        || _tabToTrade.IsReadyToTrade == false)
                    {
                        continue;
                    }

                    List<Position> positions = _tabToTrade.PositionsOpenAll;

                    if (positions.Count == 0) // позиций нет. Правда!
                    {// логика открытия позиции

                        MarketDepth md = _tabToTrade.MarketDepth.GetCopy();

                        if (md.Bids.Count == 0)
                        {
                            continue;
                        }

                        if (md.Bids.Count < _countBidsToCheck.ValueInt + 1)
                        {
                            continue;
                        }

                        decimal firstBidVolume = md.Bids[0].Bid;

                        decimal checkBidsVolume = 0;

                        for (int i = 1; i < _countBidsToCheck.ValueInt; i++)
                        {
                            checkBidsVolume += md.Bids[i].Bid;
                        }

                        if (firstBidVolume / (checkBidsVolume / 100) >= _percentInFirstBid.ValueDecimal)
                        { // первый бид больше суммы бидов на percentInFirstBid

                            decimal volume = GetVolume(_tabToTrade);

                            decimal price = md.Bids[0].Price;

                            price += _tabToTrade.Securiti.PriceStep * _slippagePriceStep.ValueInt;

                            _tabToTrade.BuyAtLimit(volume, price);
                        }
                    }
                    else
                    {// логика закрытия позиции
                        Position pos = positions[0];

                        if (pos.OpenVolume == 0)
                        {
                            continue;
                        }

                        if (pos.State != PositionStateType.Open)
                        {
                            continue;
                        }

                        if (pos.StopOrderPrice != 0)
                        {
                            continue;
                        }

                        decimal stopPrice = pos.EntryPrice - _tabToTrade.Securiti.PriceStep * _stopPriceStep.ValueInt;
                        decimal profitPrice = pos.EntryPrice + _tabToTrade.Securiti.PriceStep * _profitPriceStep.ValueInt;

                        _tabToTrade.CloseAtStopMarket(pos, stopPrice);
                        _tabToTrade.CloseAtProfitMarket(pos, profitPrice);
                    }
                }
                catch(Exception e)
                {
                    SendNewLogMessage(e.ToString(),Logging.LogMessageType.Error);
                    Thread.Sleep(5000);
                }
            }
        }

        public override string GetNameStrategyType()
        {
            return "Lesson8Bot1";
        }

        public override void ShowIndividualSettingsDialog()
        {


        }

        private decimal GetVolume(BotTabSimple tab)
        {
            decimal volume = 0;

            if (_volumeType.ValueString == "Contracts")
            {
                volume = _volume.ValueDecimal;
            }
            else if (_volumeType.ValueString == "Contract currency")
            {
                decimal contractPrice = tab.PriceBestAsk;
                volume = _volume.ValueDecimal / contractPrice;

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(tab.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                    tab.Securiti.Lot != 0 &&
                        tab.Securiti.Lot > 1)
                    {
                        volume = _volume.ValueDecimal / (contractPrice * tab.Securiti.Lot);
                    }

                    volume = Math.Round(volume, tab.Securiti.DecimalsVolume);
                }
                else // Tester or Optimizer
                {
                    volume = Math.Round(volume, 6);
                }
            }
            else if (_volumeType.ValueString == "Deposit percent")
            {
                Portfolio myPortfolio = tab.Portfolio;

                if (myPortfolio == null)
                {
                    return 0;
                }

                decimal portfolioPrimeAsset = 0;

                if (_tradeAssetInPortfolio.ValueString == "Prime")
                {
                    portfolioPrimeAsset = myPortfolio.ValueCurrent;
                }
                else
                {
                    List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();

                    if (positionOnBoard == null)
                    {
                        return 0;
                    }

                    for (int i = 0; i < positionOnBoard.Count; i++)
                    {
                        if (positionOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString)
                        {
                            portfolioPrimeAsset = positionOnBoard[i].ValueCurrent;
                            break;
                        }
                    }
                }

                if (portfolioPrimeAsset == 0)
                {
                    SendNewLogMessage("Can`t found portfolio " + _tradeAssetInPortfolio.ValueString, Logging.LogMessageType.Error);
                    return 0;
                }

                decimal moneyOnPosition = portfolioPrimeAsset * (_volume.ValueDecimal / 100);

                decimal qty = moneyOnPosition / tab.PriceBestAsk / tab.Securiti.Lot;

                if (tab.StartProgram == StartProgram.IsOsTrader)
                {
                    qty = Math.Round(qty, tab.Securiti.DecimalsVolume);
                }
                else
                {
                    qty = Math.Round(qty, 7);
                }

                return qty;
            }

            return volume;
        }

    }
}