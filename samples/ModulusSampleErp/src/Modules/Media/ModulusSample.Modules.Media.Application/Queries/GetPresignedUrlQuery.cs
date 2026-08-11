namespace ModulusSample.Modules.Media.Application.Queries;

using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Media.Application.Dtos;

public sealed record GetPresignedUrlQuery(string StoragePath, TimeSpan? Expiration = null) : IQuery<PresignedUrlResponse>;
