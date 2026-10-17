# 固定等待基线（棘轮只降不升）：由 Check-ScriptNoFixedSleep.ps1 -WriteBaseline 生成
@{
    'Agent/agent_conversation/agent_conversation_answer_local_llm_test.json' = 5
    'Agent/agent_conversation/agent_storagerun/agent_storagerun_write_trigger_test.json' = 1
    'Agent/agent_history/agent_history_state_response_task_test.json' = 3
    'Agent/agent_history/agent_history_usharp_subagent_node_test.json' = 3
    'Agent/agent_html/agent_db_batch_ops_test.json' = 1
    'Agent/agent_html/agent_html_view_switch_test.json' = 1
    'Agent/agent_local_test_gui_test.json' = 1
    'Agent/agent_mcpinvoke/agent_mcpinvoke_cline_toolcall_test.json' = 1
    'Agent/agent_mcpinvoke/agent_mcpinvoke_create_task_test.json' = 6
    'Agent/agent_real_llm_test.json' = 1
    'Agent/agent_taskpool_gate_slow_test.json' = 2
    'Agent/agent_template_load_test.json' = 1
    'Agent/agent_terminal_append_test.json' = 3
    'Agent/agent_ui_render_test.json' = 2
    'Agent/agent_usharp/agent_usharp_data_layer_test.json' = 7
    'Agent/answer_task_test.json' = 3
    'Agent/db_localdb_answer_test.json' = 4
    'Demo/simple_demo.json' = 1
    'Engine/engine_smoke_test.json' = 1
    'FileDriven/agent_hotswap_runtime_recompile_test.json' = 1
    'Produce/produce_panel_test.json' = 4
    'Produce/produce_panel_ui_render_test.json' = 1
    'UI/mainlayout_function_test.json' = 1
    'UI/mainlayout_run_ui.json' = 1
    'UI/p0_hook_verify.json' = 1
}
