// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using System.Reflection;
using UnityEngine.Rendering;

namespace UnityEditor
{
    /// Remap Viewed type to inspector type

	// 处理[CustomEditor]标签的类
	// 成员基本都是static

    // 这里不是标签
    // 标签是CustomEditor类
    internal class CustomEditorAttributes
    {
		// 保存[CustomEditor]标签，和[CanEditMultipleObjects]标签的数据
		// inspectedType -> [MonoEditorType]
        private static readonly Dictionary<Type, List<MonoEditorType>> kSCustomEditors = new Dictionary<Type, List<MonoEditorType>>();
        private static readonly Dictionary<Type, List<MonoEditorType>> kSCustomMultiEditors = new Dictionary<Type, List<MonoEditorType>>();

        private static bool s_Initialized;

		// 内部类
		// 用于存储CustomEditor标签的数据
        class MonoEditorType
        {
            public Type       m_InspectedType;
            public Type       m_InspectorType;
            public Type       m_RenderPipelineType;
            public bool       m_EditorForChildClasses;
            public bool       m_IsFallback;
        }

        internal static Type FindCustomEditorType(UnityEngine.Object o, bool multiEdit)
        {
            return FindCustomEditorTypeByType(o.GetType(), multiEdit);
        }

        private static List<MonoEditorType> s_SearchCache = new List<MonoEditorType>();

		// 寻找哪个Editor类型，用来画type
		//	* 判断是否有初始化，并进行初始化
		//	* 从本类到基类，逐步查找
		//	* 循环2遍pass，
        internal static Type FindCustomEditorTypeByType(Type type, bool multiEdit)
        {
			// 检查是否有初始化
            if (!s_Initialized)
            {
				// 遍历所有Assembly，处理所有类
                var editorAssemblies = EditorAssemblies.loadedAssemblies;
                for (int i = editorAssemblies.Length - 1; i >= 0; i--)
                    Rebuild(editorAssemblies[i]);

                s_Initialized = true;
            }

            if (type == null)
                return null;

			// 根据是否支持MultiEdit，
			// 选择处理后的数据
            var editors = multiEdit ? kSCustomMultiEditors : kSCustomEditors;

			// 循环2遍
            for (int pass = 0; pass < 2; ++pass)
            {
				// 遍历类Type，和Type的父类型
                for (Type inspected = type; inspected != null; inspected = inspected.BaseType)
                {
                    List<MonoEditorType> foundEditors;

					// 查找Type配置的Editor
                    if (!editors.TryGetValue(inspected, out foundEditors))
                    {
						// 判断是不是带T的类型
                        if (!inspected.IsGenericType)
                            continue;

						// 获取类型T
                        inspected = inspected.GetGenericTypeDefinition();

						// 再根据T来查找
                        if (!editors.TryGetValue(inspected, out foundEditors))
                            continue;
                    }

                    s_SearchCache.Clear();

					// 标记所有合适的Editor
					// 成为候选名单
                    foreach (var result in foundEditors)
                    {
						// 检查找到的Editor，是否合适
                        if (!IsAppropriateEditor(result, inspected, type != inspected, pass == 1))
                            continue;

                        s_SearchCache.Add(result);
                    }

                    Type toUse = null;

                    // we have a render pipeline...
                    // we need to select the one with the correct RP asset
					// 先根据RenderPipeline来选择，候选名称
                    if (GraphicsSettings.renderPipelineAsset != null)
                    {
                        var rpType = GraphicsSettings.renderPipelineAsset.GetType();
                        foreach (var editor in s_SearchCache)
                        {
                            if (editor.m_RenderPipelineType == rpType)
                            {
                                toUse = editor.m_InspectorType;
                                break;
                            }
                        }
                    }

					// 没有根据RenderPipeline找到需要的Editor
					// 找到第一个候选Editor

                    // no RP, fallback!
                    if (toUse == null)
                    {
                        foreach (var editor in s_SearchCache)
                        {
                            if (editor.m_RenderPipelineType == null)
                            {
                                toUse = editor.m_InspectorType;
                                break;
                            }
                        }
                    }

                    s_SearchCache.Clear();
                    if (toUse != null)
                        return toUse;
                }
            }
            return null;
        }

		// editor: 找到的Editor
		// parentClass: editor对应的类型
		// isChildClass: 找到的是type的基类的editor
		// isFallback: 是否是第二次pass
		//		* 标记了isFallback的编辑器
		//		* 只有在，没有找到标记了isFallback的编辑器，之后，才会被选择
        private static bool IsAppropriateEditor(MonoEditorType editor, Type parentClass, bool isChildClass, bool isFallback)
        {
			// 检查，基类的editor，是否支持编辑子类
            if (isChildClass && !editor.m_EditorForChildClasses)
                // skip if it's a child class and this editor doesn't want to match on children
                return false;

			// 第一遍：isFallback = false
			// 第二遍：isFallback = true
            if (isFallback != editor.m_IsFallback)
                return false;

			// Editor不是通过T找到的 ||
			// 是通过T找到的Editor，并且Editor是T类型的编辑器
			// T: 感觉这个判断有点多余，当前调用的情况下，应该总是true
            return parentClass == editor.m_InspectedType ||
                (parentClass.IsGenericType && parentClass.GetGenericTypeDefinition() == editor.m_InspectedType);
        }

		// 用来搜索Assembly中的，
		// 所有类的[CustomEditor]和[CanEditMultipleObjects]标签，标记情况
        internal static void Rebuild(Assembly assembly)
        {
			// 获取Assembly中全部类型
            Type[] types = AssemblyHelper.GetTypesFromAssembly(assembly);
            foreach (var type in types)
            {
				// 获取CustomEditor标签
				// false参数：不搜索父亲链
                object[] attrs = type.GetCustomAttributes(typeof(CustomEditor), false);

				// Q: 一个类可以有多个CustomEditor?
				// 是可以有的，应该是多个inspectedType，可以拥有相同的界面代码
                foreach (CustomEditor inspectAttr in  attrs)
                {
					// CustomEditor的数据存储
                    var t = new MonoEditorType();

					// 检查CustomEditor标签参数
                    if (inspectAttr.m_InspectedType == null)
                        Debug.Log("Can't load custom inspector " + type.Name + " because the inspected type is null.");

					// 检查CustomEditor挂载的类，是Editor
                    else if (!type.IsSubclassOf(typeof(Editor)))
                    {
						// Q: 特殊处理TweakMode?
                        // Suppress a warning on TweakMode, we did this bad in the default project folder
                        // and it's going to be too hard for customers to figure out how to fix it and also quite pointless.
                        if (type.FullName == "TweakMode" &&
							type.IsEnum &&
                            inspectAttr.m_InspectedType.FullName == "BloomAndFlares")
                            continue;

                        Debug.LogWarning(
                            type.Name +
                            " uses the CustomEditor attribute but does not inherit from Editor.\nYou must inherit from Editor. See the Editor class script documentation.");
                    }
                    else
                    {
						// 将CustomEditor标记的参数，
						// 存储到MonoEditorType结构中
                        t.m_InspectedType = inspectAttr.m_InspectedType;
                        t.m_InspectorType = type;
                        t.m_EditorForChildClasses = inspectAttr.m_EditorForChildClasses;
						// Q: CustomEditor.isFallback的含义？
                        t.m_IsFallback = inspectAttr.isFallback;

						// 特殊存储CustomEditorForRenderPipelineAttribute的参数
						// Q: CustomEditorForRenderPipelineAttribute，根据RenderPipeline来改变显示的标签
                        var attr = inspectAttr as CustomEditorForRenderPipelineAttribute;
                        if (attr != null)
                            t.m_RenderPipelineType = attr.renderPipelineType;

                        List<MonoEditorType> editors;

						// 确保dic中，有inspectedType的 List<MonoEditorType>
                        if (!kSCustomEditors.TryGetValue(inspectAttr.m_InspectedType, out editors))
                        {
                            editors = new List<MonoEditorType>();
                            kSCustomEditors[inspectAttr.m_InspectedType] = editors;
                        }

						// 将新的CustomEditor标签数据
						// 加入到列表中
                        editors.Add(t);

						// 检查是否有， [CanEditMultipleObjects]标签
                        if (type.GetCustomAttributes(typeof(CanEditMultipleObjects), false).Length > 0)
                        {
                            List<MonoEditorType> multiEditors;

							// 确保有 List<MonoEditorType>
                            if (!kSCustomMultiEditors.TryGetValue(inspectAttr.m_InspectedType, out multiEditors))
                            {
                                multiEditors = new List<MonoEditorType>();
                                kSCustomMultiEditors[inspectAttr.m_InspectedType] = multiEditors;
                            }

							// 加入到列表
                            multiEditors.Add(t);
                        }
                    }
                }
            }
        }
    }
}
