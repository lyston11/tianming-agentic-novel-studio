using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Data.Interceptors;

public sealed class UserScopeConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentUserService _currentUser;

    public UserScopeConnectionInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is NpgsqlConnection)
            SetUserScope(connection, _currentUser.TryGetUserId() ?? string.Empty);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection)
            await SetUserScopeAsync(connection, _currentUser.TryGetUserId() ?? string.Empty, cancellationToken);
    }

    private static void SetUserScope(DbConnection connection, string userId)
    {
        using var command = BuildCommand(connection, userId);
        command.ExecuteNonQuery();
    }

    private static async Task SetUserScopeAsync(
        DbConnection connection,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = BuildCommand(connection, userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand BuildCommand(DbConnection connection, string userId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_user_id', @user_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "user_id";
        parameter.Value = userId;
        command.Parameters.Add(parameter);
        return command;
    }
}
