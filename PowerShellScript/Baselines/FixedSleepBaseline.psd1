# 固定等待基线（棘轮只降不升）：由 Check-ScriptNoFixedSleep.ps1 -WriteBaseline 生成
# 事实行格式：Value + Origin（Derived=由当前扫描派生的外部基线）+ DueWhen（Debt/Verdict 必填）
@{
    'Agent/agent_conversation/agent_conversation_answer_local_llm_test.json' = @{ Value = 5; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_conversation/agent_storagerun/agent_storagerun_write_trigger_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_history/agent_history_state_response_task_test.json' = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_history/agent_history_usharp_subagent_node_test.json' = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_html/agent_db_batch_ops_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_html/agent_html_view_switch_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_local_test_gui_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_mcpinvoke/agent_mcpinvoke_cline_toolcall_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_mcpinvoke/agent_mcpinvoke_create_task_test.json' = @{ Value = 6; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_real_llm_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_taskpool_gate_slow_test.json' = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_template_load_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_terminal_append_test.json' = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_ui_render_test.json' = @{ Value = 2; Origin = 'Derived'; DueWhen = '' }
    'Agent/agent_usharp/agent_usharp_data_layer_test.json' = @{ Value = 7; Origin = 'Derived'; DueWhen = '' }
    'Agent/answer_task_test.json' = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
    'Agent/db_localdb_answer_test.json' = @{ Value = 4; Origin = 'Derived'; DueWhen = '' }
    'Demo/simple_demo.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Engine/engine_smoke_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'FileDriven/agent_hotswap_runtime_recompile_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'Produce/produce_panel_test.json' = @{ Value = 4; Origin = 'Derived'; DueWhen = '' }
    'Produce/produce_panel_ui_render_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'UI/mainlayout_function_test.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'UI/mainlayout_run_ui.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
    'UI/p0_hook_verify.json' = @{ Value = 1; Origin = 'Derived'; DueWhen = '' }
}
