﻿using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using Liquid.NET.Symbols;
using Liquid.NET.Utils;

namespace Liquid.NET
{
    public class LiquidAST
    {
        public TreeNode<IASTNode> RootNode { get; private set; }

        public LiquidAST()
        {
            RootNode = new TreeNode<IASTNode>(new RootDocumentNode());  
        } 

    }
}