using LibmpvIptvClient.Architecture.Application.Shared;
using LibmpvIptvClient.Architecture.Application.Settings;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.Settings;

public sealed record UpdateCheckInteractionResult(
    bool ShowBadge,
    bool ShouldPromptUpdate,
    string PromptMessage,
    ReleaseInfo? ReleaseInfo,
    bool ShouldShowInfo,
    string InfoMessage,
    string Title);

public sealed class SettingsUpdateActionsViewModel(
    SettingsUpdateViewModel updateViewModel,
    ILocalizationService localizationService) : ViewModelBase
{
    public async Task<UpdateCheckInteractionResult> CheckUpdateAsync(bool interactive, CancellationToken cancellationToken = default)
    {
        var title = localizationService.GetText("Common_Tips", "提示");
        var result = await updateViewModel.CheckUpdateAsync(interactive, cancellationToken).ConfigureAwait(false);

        if (result.IsNewer)
        {
            var prompt = string.Format(
                localizationService.GetText("Msg_NewVersionFound", "发现新版本 v{0}，是否查看更新？"),
                result.ReleaseInfo?.Version);
            return new UpdateCheckInteractionResult(
                true,
                interactive,
                prompt,
                result.ReleaseInfo,
                false,
                string.Empty,
                title);
        }

        if (interactive)
        {
            var msg = string.IsNullOrWhiteSpace(result.Message)
                ? localizationService.GetText("Msg_AlreadyLatest", "当前已是最新版本。")
                : result.Message;
            return new UpdateCheckInteractionResult(
                false,
                false,
                string.Empty,
                null,
                true,
                msg,
                title);
        }

        return new UpdateCheckInteractionResult(
            false,
            false,
            string.Empty,
            null,
            false,
            string.Empty,
            title);
    }
}
