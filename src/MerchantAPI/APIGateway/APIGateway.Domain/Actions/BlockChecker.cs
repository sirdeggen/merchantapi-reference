﻿// Copyright(c) 2020 Bitcoin Association.
// Distributed under the Open BSV software license, see the accompanying file LICENSE

using MerchantAPI.APIGateway.Domain.Models.Events;
using MerchantAPI.APIGateway.Domain.Repositories;
using MerchantAPI.Common.EventBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MerchantAPI.APIGateway.Domain.Actions
{
  public class BlockChecker : BackgroundService
  {
    readonly IEventBus eventBus;
    readonly ITxRepository txRepository;
    readonly ILogger<BlockChecker> logger;
    readonly AppSettings appSettings;

    public BlockChecker(IEventBus eventBus, ITxRepository txRepository, ILogger<BlockChecker> logger, IOptions<AppSettings> options)
    {
      this.eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
      this.txRepository = txRepository ?? throw new ArgumentNullException(nameof(txRepository));
      this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
      if (options == null)
      {
        throw new ArgumentNullException(nameof(logger));
      }
      appSettings = options.Value;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
      logger.LogInformation("BlockChecker background service is starting.");
      return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
      logger.LogInformation("BlockChecker background service is stopping.");
      return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      if (appSettings.DontParseBlocks.Value || appSettings.DontInsertTransactions.Value)
      {
        logger.LogInformation($"Settings `{nameof(appSettings.DontParseBlocks)}` or `{nameof(appSettings.DontInsertTransactions)}` are enabled. Blockchecker will not be started up.");
        return;
      }

      while(!stoppingToken.IsCancellationRequested)
      {
        var blocks2Parse = await txRepository.GetUnparsedBlocksAsync();

        if (blocks2Parse.Length > 0)
        {
          var blockHashes = blocks2Parse.Select(x => new uint256(x.BlockHash).ToString());
          logger.LogWarning($"Unparsed blocks found...notifying parser to parse again. BlockHashes:'{string.Join(';', blockHashes)}'");
          foreach (var block in blocks2Parse)
          {
            eventBus.Publish(new NewBlockAvailableInDB
            {
              BlockDBInternalId = block.BlockInternalId,
              BlockHash = new uint256(block.BlockHash).ToString(),
              BlockHeight = block.BlockHeight
            });
          }
        }
        await Task.Delay(new TimeSpan(0, 5, 0), stoppingToken);
      }
    }
  }
}
