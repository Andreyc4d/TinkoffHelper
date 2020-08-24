using Insight.Tinkoff.InvestSdk.Dto;
using Insight.Tinkoff.InvestSdk.Infrastructure;
using Insight.Tinkoff.InvestSdk.Infrastructure.Configurations;
using Insight.Tinkoff.InvestSdk.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Threading;

namespace TinkoffHelper
{
    class Program
    {

        public static RestConfiguration RestConfiguration;
        public static MarketService MarketService;
        public static PortfolioService PortfolioService;
        public static OperationService OperationService;

        static void Main(string[] args)
        {
            var token = File.ReadAllText("token.txt");
            RestConfiguration = new RestConfiguration
            {
                AccessToken = token,
                BaseUrl = "https://api-invest.tinkoff.ru/openapi/",
                SandboxMode = false
            };
            MarketService = new MarketService(RestConfiguration);
            PortfolioService = new PortfolioService(RestConfiguration);
            OperationService = new OperationService(RestConfiguration);

            var result = PortfolioService.GetPortfolio().Result;
            Console.WriteLine("Портфель:");
            Console.WriteLine($"Тикер     \tКол-во   \tСредняя(FIFO)\tСредняя(Норм)\tДоход(FIFO)");
            foreach (var position in result.Positions)
            {
                var operationsOfOpenPosition = new[] { new { OperationType = ExtendedOperationType.BrokerCommission, Trade = new Trade() } }.ToList();
                operationsOfOpenPosition.Clear();
                var filter = new OperationsFilter();
                filter.Figi = position.Figi;
                filter.Interval = OperationInterval.Month;
                filter.To = DateTime.Now;
                filter.From = DateTime.Now.AddYears(-1);
                var operations = OperationService.Get(filter).Result.Operations;
                var buySellOperations = operations.Where(x => (x.OperationType == ExtendedOperationType.Buy || x.OperationType == ExtendedOperationType.Sell) && x.Status != OperationStatus.Decline)
                    .SelectMany(x=> x.Trades.Select(y=> new { x.OperationType, Trade = y })).OrderByDescending(x=>x.Trade.Date);

                var balance = position.Balance;

                foreach(var operation in buySellOperations)
                {
                    balance = operation.OperationType == ExtendedOperationType.Buy ? (balance - operation.Trade.Quantity) : (balance + operation.Trade.Quantity);
                    operationsOfOpenPosition.Add(operation);
                    if (balance == 0)
                        break;
                }

                var averageValue = 0m;
                var qty = 0;
                var orderedOperations = operationsOfOpenPosition.OrderBy(x => x.Trade.Date);
                foreach (var operation in orderedOperations.OrderBy(x=>x.Trade.Date))
                {
                    if (averageValue == 0m)
                    {
                        averageValue = operation.Trade.Price;
                        qty = operation.Trade.Quantity;
                    } 
                    else if(operation.OperationType == ExtendedOperationType.Buy)
                    {
                        averageValue = (averageValue * qty + operation.Trade.Price * operation.Trade.Quantity) / (qty + operation.Trade.Quantity);
                        qty += operation.Trade.Quantity;
                    } 
                    else
                    {
                        averageValue = (averageValue * qty - operation.Trade.Price * operation.Trade.Quantity) / (qty - operation.Trade.Quantity);
                        qty -= operation.Trade.Quantity;
                    }
                }

                if (position.ExpectedYield.Value > 0)
                    Console.ForegroundColor = ConsoleColor.Green;
                else
                    Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{position.Ticker,-15}\t{position.Balance, -8}\t{position.AveragePositionPrice.Value, -8}\t{averageValue,-8:0.00}\t{position.ExpectedYield?.Value,-8} {position.ExpectedYield?.Currency,-8}");

                //if(position.Ticker == "M")
                //{
                //    foreach(var operation in orderedOperations)
                //    {
                //        Console.WriteLine($"{position.Ticker} {operation.Trade.Date} {operation.OperationType} {operation.Trade.Price}x{operation.Trade.Quantity}={operation.Trade.Price*operation.Trade.Quantity} ");
                //    }
                //}

                //foreach (var operation in operations.Where(x=>x.OperationType != ExtendedOperationType.BrokerCommission && x.Status != OperationStatus.Decline))
                //{
                //   // Console.WriteLine($"{position.Ticker} {operation.Date} {operation.OperationType} {operation.Price} {operation.Quantity} {operation.Payment} {operation.Commission?.Value} {operation.Status}");
                //}
                //var buyOperations = operations.Where(x => x.OperationType == ExtendedOperationType.Buy && x.Status != OperationStatus.Decline).SelectMany(x=>x.Trades);
                //var sellOperations = operations.Where(x => x.OperationType == ExtendedOperationType.Sell && x.Status != OperationStatus.Decline).SelectMany(x => x.Trades);
                //Console.WriteLine($"{position.Ticker} count {buyOperations.Sum(x => x.Quantity) - sellOperations.Sum(x => x.Quantity)}");

            }

        }
    }
}
