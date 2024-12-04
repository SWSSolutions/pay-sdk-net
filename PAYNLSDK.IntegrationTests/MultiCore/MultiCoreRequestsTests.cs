using PayNlSdk.IntegrationTests.Helpers;
using PayNlSdk.Sdk.Utilities;
using PayNlSdk.Sdk.V2.Client;
using PayNlSdk.Sdk.V2.DataTransferModels.Transaction;

#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace PayNlSdk.IntegrationTests.MultiCore;

public class MultiCoreRequestsTests
{
	[Fact]
	public void UninitializedClientDoesNotThrow()
	{
		MultiCorePayV2Client.IsInitialized = false;
		var client = new MultiCorePayV2Client("", "", "");
		Assert.Single(MultiCorePayV2Client.AvailableCores);
	}

	[Fact]
	public async Task TriesAll()
	{
		var httpClient = new HttpClient(new CustomHttpClientHandler());
		var client = TestHelper.CreatMultiCoreClient(httpClient);
		CustomHttpClientHandler.BlockedDomains.Add("api.pay.nl");
		Assert.Equal(3, MultiCorePayV2Client.AvailableCores.Count());

		var trx = await client.CreateTransactionMultiCore((new CreateTransactionRequest
		{
			Amount = new Amount
			{
				Value = 1000,
			},
			ReturnUrl = "https://demo.pay.nl/complete/",
		}));

		var trxInfo = await client.GetTransactionMultiCore(trx.Id!);
		Assert.Equal(trx.Id, trxInfo.Id);
		Assert.Equal(trx.Amount!.Value, trxInfo.Amount!.Value);
		Assert.Equal(trx.CreatedAt, trxInfo.CreatedAt);
	}

	[Fact]
	public void ActuallyCallsAll()
	{
		var handler = new CustomHttpClientHandler();
		var httpClient = new HttpClient(handler);
		var client = TestHelper.CreatMultiCoreClient(httpClient);
		CustomHttpClientHandler.BlockedDomains.Add("pay.nl");
		CustomHttpClientHandler.BlockedDomains.Add("payments.nl");
		CustomHttpClientHandler.BlockedDomains.Add("achterelkebetaling.nl");

		Assert.Equal(3, MultiCorePayV2Client.AvailableCores.Count());
		Assert.ThrowsAny<Exception>(() =>
		{
			_ = client.CreateTransactionMultiCore((new CreateTransactionRequest
				{
					Amount = new Amount
					{
						Value = 1000,
					},
					ReturnUrl = "https://demo.pay.nl/complete/",
				}))
				.Result;
		});

		// 2 + 1 for retry of first
		Assert.Equal(4, handler.FailedCalls.Count);
	}

	[Fact]
	public async Task FindsTrxOnSecondCoreWhenFirstComesBackOnline()
	{
		var httpClient = new HttpClient(new CustomHttpClientHandler());
		var client = TestHelper.CreatMultiCoreClient(httpClient);
		CustomHttpClientHandler.BlockedDomains.Add("api.pay.nl");

		Assert.Equal(3, MultiCorePayV2Client.AvailableCores.Count());
		var trx = await client.CreateTransactionMultiCore((new CreateTransactionRequest
		{
			Amount = new Amount
			{
				Value = 1000,
			},
			ReturnUrl = "https://demo.pay.nl/complete/",
		}));
		CustomHttpClientHandler.BlockedDomains.Clear();

		var trxInfo = await client.GetTransactionMultiCore(trx.Id!);
		Assert.Equal(trx.Id, trxInfo.Id);
		Assert.Equal(trx.Amount!.Value, trxInfo.Amount!.Value);
		Assert.Equal(trx.CreatedAt, trxInfo.CreatedAt);
	}

	[Fact]
	public async Task FindsTrxOnCore1WhenTryingC2()
	{
		var httpClient = new HttpClient(new CustomHttpClientHandler());
		var client = TestHelper.CreatMultiCoreClient(httpClient);
		Assert.Equal(0, client.ActiveEndpointIndex);
		Assert.Equal(3, MultiCorePayV2Client.AvailableCores.Count());
		var trx = await client.CreateTransactionMultiCore((new CreateTransactionRequest
		{
			Amount = new Amount
			{
				Value = 1000,
			},
			ReturnUrl = "https://demo.pay.nl/complete/",
		}));

		// Skip to core 2
		client.ActiveEndpointIndex = 1;
		var trxInfo = await client.GetTransactionMultiCore(trx.Id!);

		Assert.Equal(trx.Id, trxInfo.Id);
		Assert.Equal(trx.Amount!.Value, trxInfo.Amount!.Value);
		Assert.Equal(trx.CreatedAt, trxInfo.CreatedAt);
	}

	[Fact]
	public async Task FailedRetriesFirstCoreInNewCall()
	{
		var httpClient = new HttpClient(new CustomHttpClientHandler());

		var client = TestHelper.CreatMultiCoreClient(httpClient);
		Assert.Equal(0, client.ActiveEndpointIndex);
		Assert.Equal(3, MultiCorePayV2Client.AvailableCores.Count());
		var trx = await client.CreateTransactionMultiCore((new CreateTransactionRequest
		{
			Amount = new Amount
			{
				Value = 1000,
			},
			ReturnUrl = "https://demo.pay.nl/complete/",
		}));

		CustomHttpClientHandler.BlockedDomains.Add("rest.pay.nl");

		await Assert.ThrowsAnyAsync<Exception>(async () =>
		{
			_ = await client.GetTransactionMultiCore(trx.Id!);
		});

		CustomHttpClientHandler.BlockedDomains.Clear();

		var trxInfo = await client.GetTransactionMultiCore(trx.Id!);

		Assert.Equal(trx.Id, trxInfo.Id);
		Assert.Equal(trx.Amount!.Value, trxInfo.Amount!.Value);
		Assert.Equal(trx.CreatedAt, trxInfo.CreatedAt);
	}
}
