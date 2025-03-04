﻿// Copyright(c) 2020 Bitcoin Association.
// Distributed under the Open BSV software license, see the accompanying file LICENSE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MerchantAPI.APIGateway.Domain;
using MerchantAPI.Common.BitcoinRpc;
using MerchantAPI.Common.BitcoinRpc.Responses;
using MerchantAPI.Common.Json;
using MerchantAPI.Common.Test;
using NBitcoin;

namespace MerchantAPI.APIGateway.Test.Functional.Mock
{

  class RpcClientMock : IRpcClient
  {
    readonly RpcCallList callList;
    readonly string nodeId;
    readonly ConcurrentDictionary<uint256, byte[]> transactions;
    readonly ConcurrentDictionary<uint256, BlockWithHeight> blocks;
    readonly ConcurrentDictionary<string, object> disconnectedNodes;
    readonly ConcurrentDictionary<string, object> doNotTraceMethods;
    readonly IList<(string, int)> validScriptCombinations;
    readonly HashSet<string> ignoredTransactions;

    // Key is nodeID:memberName value is value that should be returned to the caller
    private readonly ConcurrentDictionary<string, object> predefinedResponse;

    TimeSpan requestTimeout;
    int numOfRetries;
    public TimeSpan RequestTimeout { get => requestTimeout; set => requestTimeout = value; }
    public int NumOfRetries { get => numOfRetries; set => numOfRetries = value; }
    public TimeSpan MultiRequestTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public int WaitBetweenRetriesMs { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public string zmqAddress = "tcp://127.0.0.1:28332";

    public RpcClientMock(
      RpcCallList callList,
      string host,
#pragma warning disable IDE0060 // Remove unused parameter
      int port,
      string username,
      string password,
#pragma warning restore IDE0060 // Remove unused parameter
      ConcurrentDictionary<uint256, byte[]> transactions,
      ConcurrentDictionary<uint256, BlockWithHeight> blocks,
      ConcurrentDictionary<string, object> disconnectedNodes,
      ConcurrentDictionary<string, object> doNotTraceMethods,
      ConcurrentDictionary<string, object> predefinedResponse,
      IList<(string, int)> validScriptCombinations,
      HashSet<string> ignoredTransactions
      )
    {
      this.callList = callList;
      nodeId = host;
      this.transactions = transactions;
      this.blocks = blocks;
      this.disconnectedNodes = disconnectedNodes;
      this.doNotTraceMethods = doNotTraceMethods;
      this.predefinedResponse = predefinedResponse;
      this.validScriptCombinations = validScriptCombinations;
      this.ignoredTransactions = ignoredTransactions;
    }

    public RpcClientMock(RpcCallList callList, string host, int port, string username, string password, string zmqAddress,
        ConcurrentDictionary<uint256, byte[]> transactions,
        ConcurrentDictionary<uint256, BlockWithHeight> blocks,
        ConcurrentDictionary<string, object> disconnectedNodes,
        ConcurrentDictionary<string, object> doNotTraceMethods,
        ConcurrentDictionary<string, object> predefinedResponse,
        IList<(string, int)> validScriptCombinations,
        HashSet<string> ignoredTransactions
    ) : this(callList, host, port, username, password, transactions, blocks, disconnectedNodes, doNotTraceMethods, predefinedResponse, validScriptCombinations, ignoredTransactions)
    {
      this.zmqAddress = zmqAddress;
    }

    public void ThrowIfDisconnected()
    {
      if (disconnectedNodes.ContainsKey(nodeId))
      {
        throw new HttpRequestException($"Node '{nodeId}' can not be reached (simulating error)");
      }
    }

    /// <summary>
    /// Throws if node is disconnected. Records successful call in call lists.
    /// Return non null if predefined result should be returned to called
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="txids"></param>
    /// <param name="memberName"></param>
    /// <returns></returns>
    Task<T> SimulateCallAsync<T>(string txids = null, [CallerMemberName] string memberName = "")
    {
      ThrowIfDisconnected();

      // Strip off async suffix
      const string asyncSuffix = "async";
      memberName = memberName.ToLowerInvariant();
      if (memberName.EndsWith(asyncSuffix))
      {
        memberName = memberName.Substring(0, memberName.Length - asyncSuffix.Length);
      }

      if (predefinedResponse.TryGetValue(nodeId + ":" + memberName, out var responseObj))
      {
        if (responseObj is Exception exception)
        {
          throw exception;
        }
        return Task.FromResult((T) responseObj);
      }

      if (doNotTraceMethods!=null && doNotTraceMethods.ContainsKey(memberName))
      {
        return Task.FromResult(default(T));
      }

      callList?.AddCall(memberName, nodeId, txids);

      return Task.FromResult(default(T));
    }

    public async Task<long> GetBlockCountAsync(CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<long?>();
      if (r.HasValue)
      {
        return r.Value;
      }

      return blocks.Values.OrderByDescending(x => x.Height).First().Height;
    }

    public Task<RpcGetBlockWithTxIds> GetBlockWithTxIdsAsync(string blockHash, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task<RpcGetBlock> GetBlockAsync(string blockHash, int verbosity, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public async Task<RpcBitcoinStreamReader> GetBlockAsStreamAsync(string blockHash, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcBitcoinStreamReader>();
      if (r != null)
      {
        return r;
      }
      
      if (!blocks.TryGetValue(new uint256(blockHash), out var block))
      {
        throw new Exception($"Mock block {blockHash} not found");
      }

      RpcBitcoinStreamReaderMock rpc = null;
      StreamReader str = null;
      if (block.BlockData != null)
      {
        str = new StreamReader(new MemoryStream(block.BlockData));
      }
      else if (block.StreamFilename != null)
      {
        str = new StreamReader(block.StreamFilename);
      }

      if (str != null)
      {
        rpc = new RpcBitcoinStreamReaderMock(str, token);
      }

      return rpc;
    }

    public async Task<byte[]> GetBlockByHeightAsBytesAsync(long blockHeight, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<byte[]>();
      if (r != null)
      {
        return r;
      }

      if ((blocks.Count - 1) < blockHeight)
      {
        throw new Exception($"Mock block with height {blockHeight} not found");
      }
      var block = blocks.Values.FirstOrDefault(x => x.Height == blockHeight);

      return block.BlockData;
    }

    public async Task<string> GetBlockHashAsync(long height, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<string>();
      if (r != null)
      {
        return r;
      }

      return blocks.Values.Single(x => x.Height == height).BlockHash.ToString();
    }

    public async Task<RpcGetBlockHeader> GetBlockHeaderAsync(string blockHash, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetBlockHeader>();
      if (r != null)
      {
        return r;
      }

      if (!blocks.TryGetValue(new uint256(blockHash), out var block))
      {
        throw new Exception($"Mock block {blockHash} not found");
      }

      var header = block.BlockHeader;
      var result = new RpcGetBlockHeader
      {
        Hash = blockHash,
        Confirmations = 666, // Mock
        Height = block.Height,
        Version = header.Version,
        VersionHex = header.Version.ToString("X8"),
        Merkleroot = header.HashMerkleRoot.ToString(),
        NumTx = 99999, // MOCK value,
        Time = header.BlockTime.ToUnixTimeSeconds(),
        Mediantime = header.BlockTime.ToUnixTimeSeconds(), // we can not return the right value here, sine we are not racking chain
        Nonce = header.Nonce,
        //Bits = header.Bits.ToString(),
        Difficulty = 0, // MOCK value
        Chainwork = "0", // MOCK value
        Previousblockhash = header.HashPrevBlock.ToString()
      };
      return result;
    }

    public Task<string> GetBlockHeaderAsHexAsync(string blockHash, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public async Task<RpcGetRawTransaction> GetRawTransactionAsync(string txId, int retryCount, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetRawTransaction>();
      if (r != null)
      {
        return r;
      }

      if (transactions.TryGetValue(new uint256(txId), out _))
      {
        return 
          new RpcGetRawTransaction
          {
            Txid = txId,
            // other fields are not mapped
          };
      }

      throw new Exception($"TxId {txId} not found");
    }

    public async Task<byte[]> GetRawTransactionAsBytesAsync(string txId, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<byte[]>();
      if (r != null)
      {
        return r;
      }

      if (transactions.TryGetValue(new uint256(txId), out var result))
      {
        return result;
      }
    
      throw new Exception($"TxId {txId} not found");
    }

    public async Task<string> GetBestBlockHashAsync(CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<string>();
      if (r != null)
      {
        return r;
      }

      if (blocks.IsEmpty)
      {
        throw new Exception($"No bock has been added to RpcClientMock");
      }
      return blocks.Values.OrderByDescending(x => x.Height).First().BlockHash.ToString();
    }

    public async Task<string> SendRawTransactionAsync(byte[] transaction, bool allowhighfees, bool dontCheckFees, CancellationToken? token = null)
    {
      var txId = Transaction.Parse(HelperTools.ByteToHexString(transaction), Network.Main).GetHash()
        .ToString();

      var r = await SimulateCallAsync<string>(txId);
      if (r != null)
      {
        return r;
      }

      return txId;
    }

    public async Task<RpcSendTransactions> SendRawTransactionsAsync((byte[] transaction, bool allowhighfees, bool dontCheckFees, bool listUnconfirmedAncestors, Dictionary<string, object> config)[] txs,
      CancellationToken? token = null)
    {
      var txIds = 
        string.Join('/',txs.Select(x =>
        Transaction.Parse(HelperTools.ByteToHexString(x.transaction), Network.Main).GetHash().ToString()).ToArray());

      var r = await SimulateCallAsync<RpcSendTransactions>(txIds);
      if (r != null)
      {
        return r;
      }
      else
      {        
        foreach(var tx in txs)
        {
          var txId = Transaction.Load(tx.transaction, Network.Main).GetHash(); // might not handle very large transactions
          transactions.TryAdd(txId, tx.transaction);          
        }
      }

      return new RpcSendTransactions(); // empty response means that everything was accepted
    }

    public async Task<RpcGetNetworkInfo> GetNetworkInfoAsync(CancellationToken? token=null, bool retry = false)
    {
      var r = await SimulateCallAsync<RpcGetNetworkInfo>();
      if (r != null)
      {
        return r;
      }

      return new RpcGetNetworkInfo
        {
          Version = 101001000,
          MinConsolidationFactor = 20,
          MaxConsolidationInputScriptSize = 150,
          MinConfConsolidationInput = 6,
          AcceptNonStdConsolidationInput = false
        };
    }

    public async Task<RpcDumpParameters> DumpParametersAsync(CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcDumpParameters>();
      if (r != null)
      {
        return r;
      }
      return new RpcDumpParameters();
    }

    public async Task<RpcGetTxOuts> GetTxOutsAsync(IEnumerable<(string txId, long N)> outpoints, string[] fieldList, bool includeMempool=true, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetTxOuts>();
      if (r != null)
      {
        return r;
      }

      var results = new List<PrevOut>();
      foreach (var (txId, N) in outpoints)
      {

        PrevOut result = null;
        if (transactions.TryGetValue(new uint256(txId), out var foundTx) && !ignoredTransactions.Contains(txId))
        {
          var outputs = HelperTools.ParseBytesToTransaction(foundTx).Outputs;
          if (N < outputs.Count)
          {
            var output = outputs[(int) N];
            result = new PrevOut
            {
              Error = null,
              ScriptPubKeyLength = output.ScriptPubKey.Length,
              ScriptPubKey = output.ScriptPubKey.ToHex(),
              Value = output.Value.ToDecimal(MoneyUnit.BTC),
              // Mock values - they are not correct:
              Confirmations = 0,
              IsStandard = true
            };
          }
        }

        result ??= new PrevOut
        {
          Error = "missing"
        };

        results.Add(result);
      }

      return
        new RpcGetTxOuts
        {
          TxOuts = results.ToArray()
        };
    }

    public async Task<string> SubmitBlock(byte[] block, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<string>();
      if (r != null)
      {
        return r;
      }

      return null;
    }

    public Task StopAsync(CancellationToken? token = null)
    {
      throw new NotImplementedException(); // We could add the node to list of disconnected nodes
    }

    public Task<string[]> GenerateAsync(int n, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task<string> SendToAddressAsync(string address, double amount, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public async Task<RpcGetMerkleProof> GetMerkleProofAsync(string txId, string blockHash, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetMerkleProof>();
      if (r != null)
      {
        return r;
      }

      return new RpcGetMerkleProof();
    }

    public async Task<RpcGetMerkleProof2> GetMerkleProof2Async(string txId, string blockHash, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetMerkleProof2>();
      if (r != null)
      {
        return r;
      }

      return new RpcGetMerkleProof2();
    }

    public async Task<RpcGetBlockchainInfo> GetBlockchainInfoAsync(CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetBlockchainInfo>();
      if (r != null)
      {
        return r;
      }

      if (blocks.IsEmpty)
      {
        throw new Exception($"No block has been added to RpcClientMock");
      }

      var bestBlock = blocks.Values.OrderByDescending(x => x.Height).First();
      return new RpcGetBlockchainInfo
        {
          Chain = null,
          Blocks = bestBlock.Height,
          Headers = bestBlock.Height,
          BestBlockHash = bestBlock.BlockHash.ToString()
        };
    }

    public async Task<RpcActiveZmqNotification[]> ActiveZmqNotificationsAsync(CancellationToken? token = null, bool retry = false)
    {
      var r = await SimulateCallAsync<RpcActiveZmqNotification[]>();
      if (r != null)
      {
        return r;
      }

      return ZMQTopic.RequiredZmqTopics.Select(x => new RpcActiveZmqNotification { Address = zmqAddress, Notification = x}).ToArray();
    }

    /// <summary>
    /// Note: RpcClientMock always returns empty GetRawMempool
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<string[]> GetRawMempool(CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<string[]>();
      if (r != null)
      {
        return r;
      }
      return Array.Empty<string>();
    }

    /// <summary>
    /// Note: RpcClientMock always returns empty GetMempoolAncestors
    /// </summary>
    /// <param name="txId"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<RpcGetMempoolAncestors> GetMempoolAncestors(string txId, CancellationToken? token = null)
    {
      var r = await SimulateCallAsync<RpcGetMempoolAncestors>();
      if (r != null)
      {
        return r;
      }
      return new RpcGetMempoolAncestors() { Transactions = new() };
    }

    public Task<RpcVerifyScriptResponse[]> VerifyScriptAsync(bool stopOnFirstInvalid, 
                                                                 int totalTimeoutSec,
                                                                 IEnumerable<(string Tx, int N)> dsTx, CancellationToken? token)
    {
      var results = new List<RpcVerifyScriptResponse>();
      foreach (var tx in dsTx)
      {
        if (validScriptCombinations.Contains(tx))
        {
          results.Add(new RpcVerifyScriptResponse { Result = "ok" });
        }
        else
        {
          results.Add(new RpcVerifyScriptResponse { Result = "error" });
        }
      }

      return Task.FromResult(results.ToArray());
    }

    public Task AddNodeAsync(string host, int P2PPort, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task DisconnectNodeAsync(string host, int P2PPort, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task<int> GetConnectionCountAsync(CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task<RpcListUnspent[]> ListUnspentAsync(CancellationToken? token = null, params object[] parameters)
    {
      throw new NotImplementedException();
    }

    public Task<string> DumpPrivKeyAsync(string address, CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }

    public Task<string> GetNewAddressAsync(CancellationToken? token = null)
    {
      throw new NotImplementedException();
    }
  }

}
