#pragma once
#include "tbx/systems/app/application.h"

namespace tbx_template
{
    /// @brief
    /// Purpose: Minimal default app the editor falls back to when no user project is open.
    [[tbx::app(name = "DefaultTemplate", version = "1.0.0")]];
    class DefaultTemplateApp final : public tbx::Application
    {
    };
}
