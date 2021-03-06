﻿using UnityEditor;
using UnityEngine;
using EGL = UnityEditor.EditorGUILayout;
using GL = UnityEngine.GUILayout;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using System;
using System.Reflection;
using System.Collections;
using System.Linq;

namespace AnimationNavigator {

class Navigator {

    [MenuItem("Window/Animation/Navigator %#k")]
    static void OpenNavigator() {
        var target = ReflectionInterface.inst.GetEditTarget();
        if (!target.animator) {
            Debug.Log("Animation Navigator: No editing animation target.");
            return;
        }

        var animator = target.animator;
        var clips = animator.runtimeAnimatorController.animationClips;
        var names = clips.Select(it => it.name).ToList();

        var window = EditorWindow.CreateInstance<AutoCompleteWindow>();

        window.Setup(names, str => {
            var clip = clips.Where(it => it.name == str).First();
            ReflectionInterface.inst.ChangeSelection(clip);
        });

        var animationWindow = ReflectionInterface.inst.GetFirstAnimationWindow();
        window.ShowUtility();
        window.Focus();

        window.titleContent = new GUIContent("Animation Nav");
        var pos = animationWindow.position;
        pos.height = 300;
        pos.width = 200;
        pos.center = animationWindow.position.center;
        window.position = pos;
    }

}

public class AnimationSelection {
    public Animator animator;
    public AnimationClip clip;
}

public class ReflectionInterface {
    public static readonly ReflectionInterface inst = new ReflectionInterface();

    private ReflectionInterface() {
        _tAWnd = Type.GetType("UnityEditor.AnimationWindow,UnityEditor");
        _mAWnd_GetAllAnimationWindows = _tAWnd.GetMethod("GetAllAnimationWindows");
        _pAWnd_State = _tAWnd.GetProperty("state", BindingFlags.NonPublic | BindingFlags.Instance);

        _tAWndState = Type.GetType("UnityEditorInternal.AnimationWindowState,UnityEditor");
        _pAWndState_Selection = _tAWndState.GetProperty("selection");

        _tAWndSelItem = Type.GetType("UnityEditorInternal.AnimationWindowSelectionItem,UnityEditor");
        _pAWndSelItem_animationPlayer = _tAWndSelItem.GetProperty("animationPlayer");
        _pAWndSelItem_animationClip = _tAWndSelItem.GetProperty("animationClip");
    }

    Type _tAWnd, _tAWndState, _tAWndSelItem;

    MethodInfo _mAWnd_GetAllAnimationWindows;

    PropertyInfo _pAWnd_State, _pAWndState_Selection, _pAWndSelItem_animationPlayer,
        _pAWndSelItem_animationClip;

    AnimationSelection _emptySelection = new AnimationSelection();

    public AnimationSelection GetEditTarget() {
        var window = GetFirstAnimationWindow();
        if (window == null)
            return null;

        var state = _pAWnd_State.GetValue(window, null); // AnimationWindowState
        var selection = _pAWndState_Selection.GetValue(state, null); // AnimationWindowSelectionItem
        var animationPlayer = _pAWndSelItem_animationPlayer.GetValue(selection, null); // Component
        var clip = _pAWndSelItem_animationClip.GetValue(selection, null);
        return new AnimationSelection {
            animator = animationPlayer as Animator,
            clip = clip as AnimationClip
        };
    }

    public void ChangeSelection(AnimationClip clip) {
        var window = GetFirstAnimationWindow();
        if (window == null)
            return;
        var state = _pAWnd_State.GetValue(window, null); // AnimationWindowState
        var selection = _pAWndState_Selection.GetValue(state, null) as ScriptableObject; // AnimationWindowSelectionItem

        var copy = UnityEngine.Object.Instantiate(selection);
        _pAWndSelItem_animationClip.SetValue(copy, clip, null);

        _pAWndState_Selection.SetValue(state, copy, null);
    }

    public EditorWindow GetFirstAnimationWindow() {
        var list = _mAWnd_GetAllAnimationWindows.Invoke(null, new object[0]) as ICollection;
        foreach (var c in list)
            return (EditorWindow) c;
        return null;
    }

}

class AutoCompleteWindow : EditorWindow {

    public List<string> options { get; private set; }
    public SearchField searchField { get; private set; }

    AutoCompleteTreeView treeView;

    Action<string> onFinishInput;
    
    bool _shouldClose;

    public void Setup(List<string> _options, Action<string> _finishInput) {
        options = _options;
        onFinishInput = _finishInput;

        searchField = new SearchField();
        treeView = new AutoCompleteTreeView(this, new TreeViewState());
        treeView.searchString = "";

        treeView.SetFocus();
    }

    void OnEnable() {
        Selection.selectionChanged += Close;
    }

    void OnDisable() {
        Selection.selectionChanged -= Close;
    }

    void OnGUI() {
        if (_shouldClose || treeView == null) { // When code recompiles treeview will become null
            Close();
            return;
        }
        treeView.SetFocus();

        var evt = Event.current;
        if (evt.type == EventType.KeyDown) {
            if (evt.keyCode == KeyCode.Backspace) {
                evt.Use();
                if (treeView.searchString.Length > 0) {
                    treeView.searchString = treeView.searchString.Substring(0, treeView.searchString.Length - 1);
                }
            }

            char ch = evt.character;
            if (!Char.IsControl(ch)) {
                evt.Use();
                treeView.searchString += ch;

                var rows = treeView.GetRows();
                if (rows.Count > 0) {
                    treeView.SetSelection(new List<int> { rows[0].id });
                }
            }
        }

        searchField.OnGUI(treeView.searchString);
        treeView.OnGUI(GUILayoutUtility.GetRect(0, 10000, 0, 10000));
    }

    public void ConfirmInput(int id) {
        onFinishInput(options[id]);
        Close();
    }

    public void CancelInput() {
        Close();
    }

    void OnLostFocus() {
        _shouldClose = true;
    }

}

class AutoCompleteTreeView : TreeView {

    readonly AutoCompleteWindow parent;

    public AutoCompleteTreeView(AutoCompleteWindow _parent, TreeViewState _state) : base(_state) {
        parent = _parent;
        Reload();
    }

    protected override TreeViewItem BuildRoot() {
        var root = new TreeViewItem { id = -1, depth = -1, displayName = "root" };
        for (int i = 0; i < parent.options.Count; ++i) {
            var item = new TreeViewItem {
                id = i,
                depth = 0,
                displayName = parent.options[i]
            };
            root.AddChild(item);
        }

        return root;
    }

    public void CallKeyEvent() {
        KeyEvent();
    }

    protected override void KeyEvent() {
        base.KeyEvent();

        var evt = Event.current;
        if (evt.type != EventType.KeyDown)
            return;

        if (evt.keyCode == KeyCode.Return) {
            if (state.selectedIDs.Count > 0) {
                parent.ConfirmInput(state.selectedIDs[0]);
            }
        }

        if (evt.keyCode == KeyCode.Escape) {
            parent.CancelInput();
        }
    }

    protected override bool CanMultiSelect(TreeViewItem item) {
        return false;
    }

    protected override void DoubleClickedItem(int id) {
        parent.ConfirmInput(id);
    }

}

}