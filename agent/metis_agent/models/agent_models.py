"""
Metis Agent Service Module
Implements Python-side Pydantic models, rolling-horizon planning, and browser automation via Playwright.
"""
from typing import List, Optional
from enum import Enum
from pydantic import BaseModel, Field

class RiskLevel(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"

class ActionType(str, Enum):
    LAUNCH_APP = "launch_application"
    FOCUS_WINDOW = "focus_window"
    CLICK_UI_ELEMENT = "click_ui_element"
    CLICK_COORDINATE = "click_coordinate"
    TYPE_TEXT = "type_text"
    KEYBOARD_SHORTCUT = "keyboard_shortcut"
    SCROLL = "scroll"
    READ_UI = "read_ui"
    INSPECT_SCREEN = "inspect_screen"
    NAVIGATE_BROWSER = "navigate_browser"
    BROWSER_CLICK = "browser_click"
    BROWSER_FILL = "browser_fill"
    BROWSER_EXTRACT = "browser_extract"
    CREATE_FILE = "create_file"
    READ_FILE = "read_file"
    MOVE_FILE = "move_file"
    DELETE_FILE = "delete_file"
    RUN_COMMAND = "run_command"
    WAIT = "wait"
    ASK_PERMISSION = "ask_permission"
    COMPLETE_TASK = "complete_task"

class Target(BaseModel):
    name: Optional[str] = None
    automation_id: Optional[str] = None
    x: Optional[int] = None
    y: Optional[int] = None
    selector: Optional[str] = None
    url: Optional[str] = None
    path: Optional[str] = None

class Action(BaseModel):
    type: ActionType
    target: Optional[Target] = None
    text: Optional[str] = None
    reason: str
    risk: RiskLevel = RiskLevel.LOW
    requires_confirmation: bool = False

class Plan(BaseModel):
    task_id: str
    goal: str
    subgoals: List[str] = Field(default_factory=list)
    actions: List[Action] = Field(default_factory=list)
    summary: str
