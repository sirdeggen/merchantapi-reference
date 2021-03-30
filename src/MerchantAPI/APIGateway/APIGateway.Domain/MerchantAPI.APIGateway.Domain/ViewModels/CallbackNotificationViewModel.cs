﻿// Copyright (c) 2020 Bitcoin Association

using System;
using System.Text.Json.Serialization;
using MerchantAPI.APIGateway.Domain.Models;
using MerchantAPI.Common;
using MerchantAPI.Common.BitcoinRpc.Responses;
using MerchantAPI.Common.Clock;
using MerchantAPI.Common.Json;
using NBitcoin;
using MerchantAPI.Common.Exceptions;

namespace MerchantAPI.APIGateway.Domain.ViewModels
{
  /// <summary>
  /// Base class containing fields common to all callbacks.
  /// Derived classes contains actual payload.
  /// </summary>
  public class CallbackNotificationViewModelBase
  {
    [JsonPropertyName("apiVersion")]
    public string APIVersion { get; set; }
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("minerId")]
    public string MinerId { get; set; }
    
    [JsonPropertyName("blockHash")]
    public string BlockHash { get; set; }
    
    [JsonPropertyName("blockHeight")]
    public long BlockHeight { get; set; }
    
    [JsonPropertyName("callbackTxId")]
    public string CallbackTxId { get; set; }

    [JsonPropertyName("callbackReason")]
    public string CallbackReason { get; set; }

    public static CallbackNotificationViewModelBase CreateFromNotificationData(IClock clock, NotificationData notificationData)
    {
      var txId = new uint256(notificationData.TxExternalId).ToString();
      var blockHash = (notificationData.BlockHash == null || notificationData.BlockHash.Length == 0) ? "" : new uint256(notificationData.BlockHash).ToString();
      CallbackNotificationViewModelBase callbackModel;
      switch (notificationData.NotificationType)
      {
        case Domain.CallbackReason.DoubleSpend:
        case Domain.CallbackReason.DoubleSpendAttempt:
          callbackModel = new CallbackNotificationDoubleSpendViewModel
          {
            CallbackPayload = new DsNotificationPayloadCallbackViewModel
            {
              DoubleSpendTxId = new uint256(notificationData.DoubleSpendTxId).ToString(),
              Payload = HelperTools.ByteToHexString(notificationData.Payload)
            }
          };
          break;
        case Domain.CallbackReason.MerkleProof:
          callbackModel = new CallbackNotificationMerkeProofViewModel
          {
            CallbackPayload = notificationData.MerkleProof
          };
          break;
        default:
          throw new BadRequestException("Unknown notification type.");
      }
      callbackModel.APIVersion = Const.MERCHANT_API_VERSION;
      callbackModel.BlockHash = blockHash;
      callbackModel.BlockHeight = notificationData.BlockHeight;
      callbackModel.CallbackReason = notificationData.NotificationType;
      callbackModel.CallbackTxId = txId;
      callbackModel.Timestamp = clock.UtcNow();

      return callbackModel;
    }
  }

  public class CallbackNotificationMerkeProofViewModel : CallbackNotificationViewModelBase
  {
    [JsonPropertyName("callbackPayload")]
    public RpcGetMerkleProof CallbackPayload { get; set; }
  }

  public class CallbackNotificationDoubleSpendViewModel : CallbackNotificationViewModelBase
  {
    [JsonPropertyName("callbackPayload")]
    public DsNotificationPayloadCallbackViewModel CallbackPayload { get; set; }
  }

  public class DsNotificationPayloadCallbackViewModel
  {
    [JsonPropertyName("doubleSpendTxId")]
    public string DoubleSpendTxId { get; set; }

    [JsonPropertyName("payload")]
    public string Payload { get; set; }
  }


}
