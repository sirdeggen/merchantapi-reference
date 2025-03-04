﻿// Copyright(c) 2020 Bitcoin Association.
// Distributed under the Open BSV software license, see the accompanying file LICENSE

using MerchantAPI.APIGateway.Domain.Models;
using MerchantAPI.APIGateway.Domain.Models.APIStatus;
using MerchantAPI.APIGateway.Domain.Models.Events;
using System.Threading.Tasks;

namespace MerchantAPI.APIGateway.Domain.Actions
{
  public interface IBlockParser
  {
    /// <summary>
    /// Check if database is empty and insert first block
    /// </summary>
    Task InitializeDBAsync();

    Task NewBlockDiscoveredAsync(NewBlockDiscoveredEvent e);

    BlockParserStatus GetBlockParserStatus();

    Task<int> InsertTxBlockLinkAsync(Tx[] txsToLinkToBlock, long blockInternalId);
  }
}
